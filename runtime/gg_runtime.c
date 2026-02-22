/*
 * ggLang Runtime Library - Implementation
 * Native runtime library implementation for ggLang.
 * Cross-platform: Linux, macOS, Windows
 */

#include "gg_runtime.h"
#include <ctype.h>
#include <time.h>

/* Seed random on first use */
static int gg_random_seeded = 0;
static void gg_ensure_random_seeded(void) {
    if (!gg_random_seeded) {
        srand((unsigned int)time(NULL));
        gg_random_seeded = 1;
    }
}

/* ============================================================
 * GARBAGE COLLECTOR (Mark-and-Sweep)
 * ============================================================ */

#ifndef GG_NO_GC

/** Global GC state — single instance for the entire program. */
static gg_gc_state gg_gc = {0};

void gg_gc_init(void) {
    gg_gc.heap = NULL;
    gg_gc.root_count = 0;
    gg_gc.alloc_count = 0;
    gg_gc.threshold = GG_GC_INITIAL_THRESHOLD;
    gg_gc.total_allocated = 0;
    gg_gc.total_collected = 0;
    gg_gc.collections = 0;
    gg_gc.memory_limit = 0;  /* 0 = unlimited by default */
    memset(gg_gc.roots, 0, sizeof(gg_gc.roots));
}

void gg_gc_shutdown(void) {
    /* Free every object still on the heap. */
    gg_gc_header* obj = gg_gc.heap;
    while (obj) {
        gg_gc_header* next = obj->next;
        gg_gc.total_collected += obj->size;
        free(obj);
        obj = next;
    }
    gg_gc.heap = NULL;
    gg_gc.root_count = 0;
    gg_gc.alloc_count = 0;
    gg_gc.total_allocated = 0;
}

void* gg_gc_alloc(size_t size) {
    /* Check if we should collect before allocating. */
    if (gg_gc.alloc_count >= gg_gc.threshold) {
        gg_gc_collect();
    }

    /* Check memory limit before allocating. */
    if (gg_gc.memory_limit > 0 && (gg_gc.total_allocated + size) > gg_gc.memory_limit) {
        /* Force a GC collection to try to free memory. */
        gg_gc_collect();

        /* Check again after collection. */
        if ((gg_gc.total_allocated + size) > gg_gc.memory_limit) {
            fprintf(stderr, "[ggLang GC] Fatal error: memory limit exceeded "
                    "(%zu bytes allocated, limit is %zu bytes, requested %zu bytes)\n",
                    gg_gc.total_allocated, gg_gc.memory_limit, size);
            fprintf(stderr, "[ggLang GC] The application has been terminated due to memory constraints.\n");
            fprintf(stderr, "[ggLang GC] Increase the memory limit with 'gg init --mem <size>' or optimize memory usage.\n");
            exit(137);  /* 128 + 9 (SIGKILL convention) */
        }
    }

    gg_gc_header* header = (gg_gc_header*)calloc(1, sizeof(gg_gc_header) + size);
    if (!header) {
        /* Try collecting and retrying once. */
        gg_gc_collect();
        header = (gg_gc_header*)calloc(1, sizeof(gg_gc_header) + size);
        if (!header) {
            fprintf(stderr, "[ggLang GC] Fatal error: out of memory (%zu bytes)\n", size);
            exit(1);
        }
    }

    header->size = size;
    header->marked = 0;
    header->next = gg_gc.heap;
    gg_gc.heap = header;

    gg_gc.alloc_count++;
    gg_gc.total_allocated += size;

    /* Return pointer past the header. */
    return (void*)(header + 1);
}

void gg_gc_add_root(void* root_ptr) {
    if (gg_gc.root_count >= GG_GC_MAX_ROOTS) {
        fprintf(stderr, "[ggLang GC] Warning: root set overflow, ignoring root\n");
        return;
    }
    gg_gc.roots[gg_gc.root_count++] = root_ptr;
}

void gg_gc_remove_root(void* root_ptr) {
    for (int i = 0; i < gg_gc.root_count; i++) {
        if (gg_gc.roots[i] == root_ptr) {
            /* Shift remaining roots down. */
            for (int j = i; j < gg_gc.root_count - 1; j++) {
                gg_gc.roots[j] = gg_gc.roots[j + 1];
            }
            gg_gc.root_count--;
            return;
        }
    }
}

/**
 * Checks if a pointer belongs to a GC-managed object.
 * Returns the header if found, NULL otherwise.
 */
static gg_gc_header* gg_gc_find_header(void* ptr) {
    if (!ptr) return NULL;
    gg_gc_header* candidate = ((gg_gc_header*)ptr) - 1;
    /* Walk the heap to verify this is a real GC object. */
    gg_gc_header* obj = gg_gc.heap;
    while (obj) {
        if (obj == candidate) return obj;
        obj = obj->next;
    }
    return NULL;
}

/**
 * Marks a single object as reachable.
 * Scans the object's memory for pointers to other GC objects (conservative).
 */
static void gg_gc_mark_object(gg_gc_header* header) {
    if (!header || header->marked) return;
    header->marked = 1;

    /*
     * Conservative scan: treat every pointer-aligned word in the
     * object's body as a potential pointer. If it points into a
     * GC-managed object, mark that object too.
     */
    void** body = (void**)(header + 1);
    size_t word_count = header->size / sizeof(void*);
    for (size_t i = 0; i < word_count; i++) {
        void* candidate = body[i];
        if (candidate) {
            gg_gc_header* target = gg_gc_find_header(candidate);
            if (target && !target->marked) {
                gg_gc_mark_object(target);
            }
        }
    }
}

/**
 * Mark phase: scan all roots and mark reachable objects.
 */
static void gg_gc_mark(void) {
    for (int i = 0; i < gg_gc.root_count; i++) {
        void* root_ptr = gg_gc.roots[i];
        if (!root_ptr) continue;

        /*
         * A root is a pointer TO a variable that holds a GC pointer.
         * Dereference it to get the actual object pointer.
         */
        void* obj_ptr = *((void**)root_ptr);
        if (obj_ptr) {
            gg_gc_header* header = gg_gc_find_header(obj_ptr);
            if (header) {
                gg_gc_mark_object(header);
            }
        }
    }
}

/**
 * Sweep phase: free all unmarked objects, reset marks on survivors.
 */
static void gg_gc_sweep(void) {
    gg_gc_header** ptr = &gg_gc.heap;
    while (*ptr) {
        if (!(*ptr)->marked) {
            /* Unreachable — free it. */
            gg_gc_header* unreachable = *ptr;
            *ptr = unreachable->next;
            gg_gc.total_allocated -= unreachable->size;
            gg_gc.total_collected += unreachable->size;
            free(unreachable);
        } else {
            /* Reachable — clear mark for next cycle. */
            (*ptr)->marked = 0;
            ptr = &(*ptr)->next;
        }
    }
}

void gg_gc_collect(void) {
    gg_gc_mark();
    gg_gc_sweep();
    gg_gc.alloc_count = 0;
    gg_gc.collections++;

    /* Adaptive threshold: grow if most objects survive. */
    size_t live_count = 0;
    gg_gc_header* obj = gg_gc.heap;
    while (obj) { live_count++; obj = obj->next; }

    if (live_count > gg_gc.threshold / 2) {
        gg_gc.threshold *= 2;
    }
}

void gg_gc_set_memory_limit(size_t limit_bytes) {
    gg_gc.memory_limit = limit_bytes;
}

gg_gc_state* gg_gc_get_state(void) {
    return &gg_gc;
}

/* ============================================================
 * MEMORY MANAGEMENT (Legacy API — delegates to GC)
 * ============================================================ */

void* gg_alloc(size_t size) {
    return gg_gc_alloc(size);
}

void gg_free(void* ptr) {
    if (!ptr) return;
    /*
     * Remove from the GC heap list so the collector won't touch it,
     * then free the underlying allocation (header + body).
     */
    gg_gc_header* target = ((gg_gc_header*)ptr) - 1;
    gg_gc_header** p = &gg_gc.heap;
    while (*p) {
        if (*p == target) {
            *p = target->next;
            gg_gc.total_allocated -= target->size;
            free(target);
            return;
        }
        p = &(*p)->next;
    }
    /* Fallback: not tracked by GC, free directly. */
    free(ptr);
}

void gg_retain(void* ptr) {
    /* Retained for API compatibility — no-op with GC. */
    (void)ptr;
}

void gg_release(void* ptr) {
    /* Retained for API compatibility — no-op with GC. */
    (void)ptr;
}

void Memory_free(void* ptr) {
    gg_free(ptr);
}

void* Memory_alloc(size_t size) {
    return gg_alloc(size);
}

#endif /* !GG_NO_GC */

/* ============================================================
 * STRING
 * ============================================================ */

gg_string* gg_string_from_cstr(const char* cstr) {
    if (!cstr) cstr = "";

    gg_string* s = (gg_string*)gg_alloc(sizeof(gg_string));
    s->ref_count = 1;
    s->length = (int32_t)strlen(cstr);
    s->data = (char*)malloc(s->length + 1);
    if (!s->data) {
        fprintf(stderr, "[ggLang] Fatal error: string allocation failed\n");
        exit(1);
    }
    memcpy(s->data, cstr, s->length + 1);
    return s;
}

gg_string* gg_string_from_buf(const char* buf, int32_t length) {
    gg_string* s = (gg_string*)gg_alloc(sizeof(gg_string));
    s->ref_count = 1;
    s->length = length;
    s->data = (char*)malloc(length + 1);
    if (!s->data) {
        fprintf(stderr, "[ggLang] Fatal error: string allocation failed\n");
        exit(1);
    }
    memcpy(s->data, buf, length);
    s->data[length] = '\0';
    return s;
}

const char* gg_string_cstr(gg_string* s) {
    return s ? s->data : "";
}

gg_string* gg_string_concat(gg_string* a, gg_string* b) {
    if (!a) return b ? b : gg_string_from_cstr("");
    if (!b) return a;

    int32_t new_length = a->length + b->length;
    char* buf = (char*)malloc(new_length + 1);
    if (!buf) {
        fprintf(stderr, "[ggLang] Fatal error: string allocation failed\n");
        exit(1);
    }

    memcpy(buf, a->data, a->length);
    memcpy(buf + a->length, b->data, b->length);
    buf[new_length] = '\0';

    gg_string* result = (gg_string*)gg_alloc(sizeof(gg_string));
    result->ref_count = 1;
    result->length = new_length;
    result->data = buf;
    return result;
}

int gg_string_equals(gg_string* a, gg_string* b) {
    if (a == b) return 1;
    if (!a || !b) return 0;
    if (a->length != b->length) return 0;
    return memcmp(a->data, b->data, a->length) == 0;
}

int32_t gg_string_length(gg_string* s) {
    return s ? s->length : 0;
}

gg_string* gg_string_substring(gg_string* s, int32_t start, int32_t length) {
    if (!s || start < 0 || start >= s->length) return gg_string_from_cstr("");
    if (start + length > s->length) length = s->length - start;
    return gg_string_from_buf(s->data + start, length);
}

int gg_string_contains(gg_string* s, gg_string* sub) {
    if (!s || !sub) return 0;
    return strstr(s->data, sub->data) != NULL;
}

gg_string* gg_string_toUpper(gg_string* s) {
    if (!s) return gg_string_from_cstr("");
    char* buf = (char*)malloc(s->length + 1);
    for (int32_t i = 0; i < s->length; i++) {
        buf[i] = (char)toupper((unsigned char)s->data[i]);
    }
    buf[s->length] = '\0';
    gg_string* result = gg_string_from_buf(buf, s->length);
    free(buf);
    return result;
}

gg_string* gg_string_toLower(gg_string* s) {
    if (!s) return gg_string_from_cstr("");
    char* buf = (char*)malloc(s->length + 1);
    for (int32_t i = 0; i < s->length; i++) {
        buf[i] = (char)tolower((unsigned char)s->data[i]);
    }
    buf[s->length] = '\0';
    gg_string* result = gg_string_from_buf(buf, s->length);
    free(buf);
    return result;
}

gg_string* gg_string_trim(gg_string* s) {
    if (!s || s->length == 0) return gg_string_from_cstr("");

    int32_t start = 0;
    int32_t end = s->length - 1;

    while (start <= end && isspace((unsigned char)s->data[start])) start++;
    while (end >= start && isspace((unsigned char)s->data[end])) end--;

    return gg_string_from_buf(s->data + start, end - start + 1);
}

int32_t gg_string_indexOf(gg_string* s, gg_string* sub) {
    if (!s || !sub) return -1;
    char* found = strstr(s->data, sub->data);
    return found ? (int32_t)(found - s->data) : -1;
}

gg_string* gg_string_replace(gg_string* s, gg_string* old_str, gg_string* new_str) {
    if (!s || !old_str || old_str->length == 0) return s;
    if (!new_str) new_str = gg_string_from_cstr("");

    /* Count occurrences */
    int count = 0;
    char* pos = s->data;
    while ((pos = strstr(pos, old_str->data)) != NULL) {
        count++;
        pos += old_str->length;
    }

    if (count == 0) return gg_string_from_cstr(s->data);

    int32_t new_length = s->length + count * (new_str->length - old_str->length);
    char* buf = (char*)malloc(new_length + 1);
    char* dest = buf;
    pos = s->data;

    while (*pos) {
        char* found = strstr(pos, old_str->data);
        if (found) {
            int32_t before = (int32_t)(found - pos);
            memcpy(dest, pos, before);
            dest += before;
            memcpy(dest, new_str->data, new_str->length);
            dest += new_str->length;
            pos = found + old_str->length;
        } else {
            strcpy(dest, pos);
            break;
        }
    }
    buf[new_length] = '\0';

    gg_string* result = gg_string_from_buf(buf, new_length);
    free(buf);
    return result;
}

gg_string* gg_string_toString(gg_string* s) {
    return s;
}

/* ============================================================
 * STRING CONVERSIONS
 * ============================================================ */

gg_string* gg_int_to_string(int32_t value) {
    char buf[32];
    snprintf(buf, sizeof(buf), "%d", value);
    return gg_string_from_cstr(buf);
}

gg_string* gg_long_to_string(int64_t value) {
    char buf[32];
    snprintf(buf, sizeof(buf), "%ld", value);
    return gg_string_from_cstr(buf);
}

gg_string* gg_float_to_string(float value) {
    char buf[64];
    snprintf(buf, sizeof(buf), "%g", value);
    return gg_string_from_cstr(buf);
}

gg_string* gg_double_to_string(double value) {
    char buf[64];
    snprintf(buf, sizeof(buf), "%g", value);
    return gg_string_from_cstr(buf);
}

gg_string* gg_bool_to_string(int value) {
    return gg_string_from_cstr(value ? "true" : "false");
}

gg_string* gg_char_to_string(char value) {
    char buf[2] = { value, '\0' };
    return gg_string_from_cstr(buf);
}

/* ============================================================
 * DYNAMIC ARRAY
 * ============================================================ */

gg_array* gg_array_new(int32_t elem_size, int32_t initial_size) {
    gg_array* arr = (gg_array*)gg_alloc(sizeof(gg_array));
    arr->ref_count = 1;
    arr->length = initial_size;
    arr->capacity = initial_size > 0 ? initial_size : 8;
    arr->elem_size = elem_size;
    arr->data = calloc(arr->capacity, elem_size);
    if (!arr->data) {
        fprintf(stderr, "[ggLang] Fatal error: array allocation failed\n");
        exit(1);
    }
    return arr;
}

int32_t gg_array_length(gg_array* arr) {
    return arr ? arr->length : 0;
}

void* gg_array_get_ptr(gg_array* arr, int32_t index) {
    if (!arr || index < 0 || index >= arr->length) {
        fprintf(stderr, "[ggLang] Error: array index out of bounds (index=%d, length=%d)\n",
                index, arr ? arr->length : 0);
        exit(1);
    }
    return (char*)arr->data + (index * arr->elem_size);
}

void gg_array_set(gg_array* arr, int32_t index, void* value) {
    if (!arr || index < 0 || index >= arr->length) {
        fprintf(stderr, "[ggLang] Error: array index out of bounds (index=%d, length=%d)\n",
                index, arr ? arr->length : 0);
        exit(1);
    }
    memcpy((char*)arr->data + (index * arr->elem_size), value, arr->elem_size);
}

/* ============================================================
 * CONSOLE I/O
 * ============================================================ */

void gg_console_writeLine(gg_string* s) {
    if (s && s->data) {
        printf("%s\n", s->data);
    } else {
        printf("\n");
    }
    fflush(stdout);
}

void gg_console_write(gg_string* s) {
    if (s && s->data) {
        printf("%s", s->data);
    }
    fflush(stdout);
}

gg_string* gg_console_readLine(void) {
    char buf[4096];
    if (fgets(buf, sizeof(buf), stdin)) {
        /* Remove newline */
        size_t len = strlen(buf);
        if (len > 0 && buf[len - 1] == '\n') {
            buf[len - 1] = '\0';
        }
        return gg_string_from_cstr(buf);
    }
    return gg_string_from_cstr("");
}

int32_t gg_console_readInt(void) {
    int32_t value = 0;
    if (scanf("%d", &value) != 1) {
        fprintf(stderr, "[ggLang] Warning: failed to read integer from stdin\n");
    }
    /* Clear buffer */
    int c;
    while ((c = getchar()) != '\n' && c != EOF);
    return value;
}

/* ============================================================
 * MATH FUNCTIONS
 * ============================================================ */

double gg_math_abs(double x) { return fabs(x); }
double gg_math_sqrt(double x) { return sqrt(x); }
double gg_math_pow(double base_val, double exp) { return pow(base_val, exp); }
double gg_math_min(double a, double b) { return a < b ? a : b; }
double gg_math_max(double a, double b) { return a > b ? a : b; }
int32_t gg_math_floor(double x) { return (int32_t)floor(x); }
int32_t gg_math_ceil(double x) { return (int32_t)ceil(x); }
double gg_math_sin(double x) { return sin(x); }
double gg_math_cos(double x) { return cos(x); }
double gg_math_tan(double x) { return tan(x); }
double gg_math_log(double x) { return log(x); }

/* ============================================================
 * FILE I/O
 * ============================================================ */

gg_string* gg_files_readAll(gg_string* path) {
    if (!path) return gg_string_from_cstr("");
    FILE* f = fopen(path->data, "rb");
    if (!f) return gg_string_from_cstr("");

    fseek(f, 0, SEEK_END);
    long size = ftell(f);
    fseek(f, 0, SEEK_SET);

    if (size < 0) { fclose(f); return gg_string_from_cstr(""); }

    char* buf = (char*)malloc(size + 1);
    if (!buf) { fclose(f); return gg_string_from_cstr(""); }

    size_t read = fread(buf, 1, size, f);
    buf[read] = '\0';
    fclose(f);

    gg_string* result = gg_string_from_buf(buf, (int32_t)read);
    free(buf);
    return result;
}

int gg_files_writeAll(gg_string* path, gg_string* content) {
    if (!path) return 0;
    FILE* f = fopen(path->data, "wb");
    if (!f) return 0;
    if (content && content->length > 0) {
        fwrite(content->data, 1, content->length, f);
    }
    fclose(f);
    return 1;
}

int gg_files_append(gg_string* path, gg_string* content) {
    if (!path) return 0;
    FILE* f = fopen(path->data, "ab");
    if (!f) return 0;
    if (content && content->length > 0) {
        fwrite(content->data, 1, content->length, f);
    }
    fclose(f);
    return 1;
}

int gg_files_exists(gg_string* path) {
    if (!path) return 0;
#ifdef GG_PLATFORM_WINDOWS
    return _access(path->data, 0) == 0;
#else
    return access(path->data, F_OK) == 0;
#endif
}

int gg_files_delete(gg_string* path) {
    if (!path) return 0;
    return remove(path->data) == 0;
}

int gg_files_copy(gg_string* source, gg_string* dest) {
    if (!source || !dest) return 0;
    FILE* src = fopen(source->data, "rb");
    if (!src) return 0;
    FILE* dst = fopen(dest->data, "wb");
    if (!dst) { fclose(src); return 0; }

    char buf[8192];
    size_t n;
    while ((n = fread(buf, 1, sizeof(buf), src)) > 0) {
        fwrite(buf, 1, n, dst);
    }
    fclose(src);
    fclose(dst);
    return 1;
}

int gg_files_move(gg_string* source, gg_string* dest) {
    if (!source || !dest) return 0;
    return rename(source->data, dest->data) == 0;
}

int32_t gg_files_size(gg_string* path) {
    if (!path) return -1;
#ifdef GG_PLATFORM_WINDOWS
    struct _stat st;
    if (_stat(path->data, &st) != 0) return -1;
    return (int32_t)st.st_size;
#else
    struct stat st;
    if (stat(path->data, &st) != 0) return -1;
    return (int32_t)st.st_size;
#endif
}

int gg_directory_exists(gg_string* path) {
    if (!path) return 0;
#ifdef GG_PLATFORM_WINDOWS
    struct _stat st;
    if (_stat(path->data, &st) != 0) return 0;
    return (st.st_mode & _S_IFDIR) != 0;
#else
    struct stat st;
    if (stat(path->data, &st) != 0) return 0;
    return S_ISDIR(st.st_mode);
#endif
}

int gg_directory_create(gg_string* path) {
    if (!path) return 0;
#ifdef GG_PLATFORM_WINDOWS
    return _mkdir(path->data) == 0;
#else
    return mkdir(path->data, 0755) == 0;
#endif
}

int gg_directory_remove(gg_string* path) {
    if (!path) return 0;
#ifdef GG_PLATFORM_WINDOWS
    return _rmdir(path->data) == 0;
#else
    return rmdir(path->data) == 0;
#endif
}

gg_string* gg_directory_getCurrent(void) {
    char buf[4096];
#ifdef GG_PLATFORM_WINDOWS
    if (_getcwd(buf, sizeof(buf))) return gg_string_from_cstr(buf);
#else
    if (getcwd(buf, sizeof(buf))) return gg_string_from_cstr(buf);
#endif
    return gg_string_from_cstr("");
}

int gg_directory_setCurrent(gg_string* path) {
    if (!path) return 0;
#ifdef GG_PLATFORM_WINDOWS
    return _chdir(path->data) == 0;
#else
    return chdir(path->data) == 0;
#endif
}

gg_string* gg_path_combine(gg_string* a, gg_string* b) {
    if (!a || a->length == 0) return b ? b : gg_string_from_cstr("");
    if (!b || b->length == 0) return a;
    gg_string* sep = gg_string_from_cstr(GG_PATH_SEP);
    gg_string* tmp = gg_string_concat(a, sep);
    return gg_string_concat(tmp, b);
}

gg_string* gg_path_getFileName(gg_string* path) {
    if (!path || path->length == 0) return gg_string_from_cstr("");
    int last = -1;
    for (int32_t i = 0; i < path->length; i++) {
        if (path->data[i] == '/' || path->data[i] == '\\') last = i;
    }
    if (last < 0) return gg_string_from_cstr(path->data);
    return gg_string_from_cstr(path->data + last + 1);
}

gg_string* gg_path_getExtension(gg_string* path) {
    if (!path || path->length == 0) return gg_string_from_cstr("");
    int dot = -1;
    for (int32_t i = path->length - 1; i >= 0; i--) {
        if (path->data[i] == '.') { dot = i; break; }
        if (path->data[i] == '/' || path->data[i] == '\\') break;
    }
    if (dot < 0) return gg_string_from_cstr("");
    return gg_string_from_cstr(path->data + dot);
}

gg_string* gg_path_getDirectory(gg_string* path) {
    if (!path || path->length == 0) return gg_string_from_cstr("");
    int last = -1;
    for (int32_t i = 0; i < path->length; i++) {
        if (path->data[i] == '/' || path->data[i] == '\\') last = i;
    }
    if (last < 0) return gg_string_from_cstr("");
    return gg_string_from_buf(path->data, last);
}

/* ============================================================
 * CRYPTOGRAPHY - SHA-256
 * ============================================================ */

static void gg_sha256_transform(uint32_t state[8], const uint8_t block[64]) {
    static const uint32_t k[64] = {
        0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
        0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
        0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
        0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
        0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
        0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
        0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
        0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2
    };

    #define RR(x, n) (((x) >> (n)) | ((x) << (32-(n))))
    #define CH(x,y,z) (((x)&(y))^((~(x))&(z)))
    #define MAJ(x,y,z) (((x)&(y))^((x)&(z))^((y)&(z)))
    #define EP0(x) (RR(x,2)^RR(x,13)^RR(x,22))
    #define EP1(x) (RR(x,6)^RR(x,11)^RR(x,25))
    #define SIG0(x) (RR(x,7)^RR(x,18)^((x)>>3))
    #define SIG1(x) (RR(x,17)^RR(x,19)^((x)>>10))

    uint32_t w[64], a, b, c, d, e, f, g, h, t1, t2;
    int i;
    for (i = 0; i < 16; i++)
        w[i] = ((uint32_t)block[i*4]<<24)|((uint32_t)block[i*4+1]<<16)|((uint32_t)block[i*4+2]<<8)|block[i*4+3];
    for (i = 16; i < 64; i++)
        w[i] = SIG1(w[i-2]) + w[i-7] + SIG0(w[i-15]) + w[i-16];

    a=state[0]; b=state[1]; c=state[2]; d=state[3];
    e=state[4]; f=state[5]; g=state[6]; h=state[7];

    for (i = 0; i < 64; i++) {
        t1 = h + EP1(e) + CH(e,f,g) + k[i] + w[i];
        t2 = EP0(a) + MAJ(a,b,c);
        h=g; g=f; f=e; e=d+t1; d=c; c=b; b=a; a=t1+t2;
    }
    state[0]+=a; state[1]+=b; state[2]+=c; state[3]+=d;
    state[4]+=e; state[5]+=f; state[6]+=g; state[7]+=h;

    #undef RR
    #undef CH
    #undef MAJ
    #undef EP0
    #undef EP1
    #undef SIG0
    #undef SIG1
}

gg_string* gg_crypto_sha256(gg_string* input) {
    if (!input) return gg_string_from_cstr("");
    uint32_t state[8] = {
        0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,
        0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19
    };
    const uint8_t* data = (const uint8_t*)input->data;
    size_t len = input->length;
    size_t i;

    /* Process full 64-byte blocks */
    for (i = 0; i + 64 <= len; i += 64)
        gg_sha256_transform(state, data + i);

    /* Padding */
    uint8_t block[64];
    size_t remain = len - i;
    memcpy(block, data + i, remain);
    block[remain++] = 0x80;
    if (remain > 56) {
        memset(block + remain, 0, 64 - remain);
        gg_sha256_transform(state, block);
        memset(block, 0, 56);
    } else {
        memset(block + remain, 0, 56 - remain);
    }
    uint64_t bits = (uint64_t)len * 8;
    for (int j = 7; j >= 0; j--)
        block[56 + (7-j)] = (uint8_t)(bits >> (j*8));
    gg_sha256_transform(state, block);

    char hex[65];
    for (i = 0; i < 8; i++)
        snprintf(hex + i*8, 9, "%08x", state[i]);
    return gg_string_from_cstr(hex);
}

/* ============================================================
 * CRYPTOGRAPHY - MD5
 * ============================================================ */

static void gg_md5_transform(uint32_t state[4], const uint8_t block[64]) {
    static const uint32_t S[64] = {
        7,12,17,22,7,12,17,22,7,12,17,22,7,12,17,22,
        5,9,14,20,5,9,14,20,5,9,14,20,5,9,14,20,
        4,11,16,23,4,11,16,23,4,11,16,23,4,11,16,23,
        6,10,15,21,6,10,15,21,6,10,15,21,6,10,15,21
    };
    static const uint32_t K[64] = {
        0xd76aa478,0xe8c7b756,0x242070db,0xc1bdceee,0xf57c0faf,0x4787c62a,0xa8304613,0xfd469501,
        0x698098d8,0x8b44f7af,0xffff5bb1,0x895cd7be,0x6b901122,0xfd987193,0xa679438e,0x49b40821,
        0xf61e2562,0xc040b340,0x265e5a51,0xe9b6c7aa,0xd62f105d,0x02441453,0xd8a1e681,0xe7d3fbc8,
        0x21e1cde6,0xc33707d6,0xf4d50d87,0x455a14ed,0xa9e3e905,0xfcefa3f8,0x676f02d9,0x8d2a4c8a,
        0xfffa3942,0x8771f681,0x6d9d6122,0xfde5380c,0xa4beea44,0x4bdecfa9,0xf6bb4b60,0xbebfbc70,
        0x289b7ec6,0xeaa127fa,0xd4ef3085,0x04881d05,0xd9d4d039,0xe6db99e5,0x1fa27cf8,0xc4ac5665,
        0xf4292244,0x432aff97,0xab9423a7,0xfc93a039,0x655b59c3,0x8f0ccc92,0xffeff47d,0x85845dd1,
        0x6fa87e4f,0xfe2ce6e0,0xa3014314,0x4e0811a1,0xf7537e82,0xbd3af235,0x2ad7d2bb,0xeb86d391
    };

    uint32_t M[16];
    for (int i = 0; i < 16; i++)
        M[i] = ((uint32_t)block[i*4]) | ((uint32_t)block[i*4+1]<<8) | ((uint32_t)block[i*4+2]<<16) | ((uint32_t)block[i*4+3]<<24);

    uint32_t a=state[0], b=state[1], c=state[2], d=state[3];
    for (int i = 0; i < 64; i++) {
        uint32_t F, g;
        if (i < 16)      { F = (b&c)|((~b)&d); g = i; }
        else if (i < 32) { F = (d&b)|((~d)&c); g = (5*i+1)%16; }
        else if (i < 48) { F = b^c^d;           g = (3*i+5)%16; }
        else              { F = c^(b|(~d));      g = (7*i)%16; }
        uint32_t tmp = d;
        d = c; c = b;
        uint32_t x = a + F + K[i] + M[g];
        b = b + ((x << S[i]) | (x >> (32-S[i])));
        a = tmp;
    }
    state[0]+=a; state[1]+=b; state[2]+=c; state[3]+=d;
}

gg_string* gg_crypto_md5(gg_string* input) {
    if (!input) return gg_string_from_cstr("");
    uint32_t state[4] = {0x67452301, 0xefcdab89, 0x98badcfe, 0x10325476};
    const uint8_t* data = (const uint8_t*)input->data;
    size_t len = input->length;
    size_t i;

    for (i = 0; i + 64 <= len; i += 64)
        gg_md5_transform(state, data + i);

    uint8_t block[64];
    size_t remain = len - i;
    memcpy(block, data + i, remain);
    block[remain++] = 0x80;
    if (remain > 56) {
        memset(block + remain, 0, 64 - remain);
        gg_md5_transform(state, block);
        memset(block, 0, 56);
    } else {
        memset(block + remain, 0, 56 - remain);
    }
    uint64_t bits = (uint64_t)len * 8;
    memcpy(block + 56, &bits, 8);  /* little-endian */
    gg_md5_transform(state, block);

    char hex[33];
    for (i = 0; i < 4; i++) {
        snprintf(hex + i*8, 9, "%02x%02x%02x%02x",
            state[i]&0xff, (state[i]>>8)&0xff, (state[i]>>16)&0xff, (state[i]>>24)&0xff);
    }
    return gg_string_from_cstr(hex);
}

/* ============================================================
 * CRYPTOGRAPHY - SHA-1
 * ============================================================ */

gg_string* gg_crypto_sha1(gg_string* input) {
    if (!input) return gg_string_from_cstr("");
    uint32_t h0=0x67452301, h1=0xEFCDAB89, h2=0x98BADCFE, h3=0x10325476, h4=0xC3D2E1F0;
    const uint8_t* data = (const uint8_t*)input->data;
    size_t len = input->length;

    /* Prepare padded message */
    size_t padded_len = ((len + 8) / 64 + 1) * 64;
    uint8_t* msg = (uint8_t*)calloc(padded_len, 1);
    memcpy(msg, data, len);
    msg[len] = 0x80;
    uint64_t bits = (uint64_t)len * 8;
    for (int i = 0; i < 8; i++)
        msg[padded_len - 1 - i] = (uint8_t)(bits >> (i*8));

    for (size_t chunk = 0; chunk < padded_len; chunk += 64) {
        uint32_t w[80];
        for (int i = 0; i < 16; i++)
            w[i] = ((uint32_t)msg[chunk+i*4]<<24)|((uint32_t)msg[chunk+i*4+1]<<16)|
                   ((uint32_t)msg[chunk+i*4+2]<<8)|msg[chunk+i*4+3];
        for (int i = 16; i < 80; i++) {
            uint32_t v = w[i-3]^w[i-8]^w[i-14]^w[i-16];
            w[i] = (v<<1)|(v>>31);
        }

        uint32_t a=h0,b=h1,c=h2,d=h3,e=h4;
        for (int i = 0; i < 80; i++) {
            uint32_t f, k;
            if (i<20)      { f=(b&c)|((~b)&d); k=0x5A827999; }
            else if (i<40) { f=b^c^d;           k=0x6ED9EBA1; }
            else if (i<60) { f=(b&c)|(b&d)|(c&d); k=0x8F1BBCDC; }
            else           { f=b^c^d;           k=0xCA62C1D6; }
            uint32_t tmp = ((a<<5)|(a>>27)) + f + e + k + w[i];
            e=d; d=c; c=(b<<30)|(b>>2); b=a; a=tmp;
        }
        h0+=a; h1+=b; h2+=c; h3+=d; h4+=e;
    }
    free(msg);

    char hex[41];
    snprintf(hex, 41, "%08x%08x%08x%08x%08x", h0, h1, h2, h3, h4);
    return gg_string_from_cstr(hex);
}

/* ============================================================
 * CRYPTOGRAPHY - CRC32
 * ============================================================ */

gg_string* gg_crypto_crc32(gg_string* input) {
    if (!input) return gg_string_from_cstr("00000000");
    uint32_t crc = 0xFFFFFFFF;
    for (int32_t i = 0; i < input->length; i++) {
        crc ^= (uint8_t)input->data[i];
        for (int j = 0; j < 8; j++)
            crc = (crc >> 1) ^ (0xEDB88320 & (-(crc & 1)));
    }
    crc ^= 0xFFFFFFFF;
    char hex[9];
    snprintf(hex, 9, "%08x", crc);
    return gg_string_from_cstr(hex);
}

/* ============================================================
 * CRYPTOGRAPHY - HMAC-SHA256
 * ============================================================ */

gg_string* gg_crypto_hmacSha256(gg_string* input, gg_string* key) {
    if (!input || !key) return gg_string_from_cstr("");

    uint8_t k_pad[64];
    memset(k_pad, 0, 64);

    /* If key > 64 bytes, hash it first */
    if (key->length > 64) {
        gg_string* hashed = gg_crypto_sha256(key);
        /* Convert hex to bytes */
        for (int i = 0; i < 32 && i*2+1 < hashed->length; i++) {
            unsigned int byte;
            char h[3] = {hashed->data[i*2], hashed->data[i*2+1], 0};
            sscanf(h, "%02x", &byte);
            k_pad[i] = (uint8_t)byte;
        }
    } else {
        memcpy(k_pad, key->data, key->length);
    }

    /* Inner padding */
    uint8_t i_pad[64];
    for (int i = 0; i < 64; i++) i_pad[i] = k_pad[i] ^ 0x36;

    /* Outer padding */
    uint8_t o_pad[64];
    for (int i = 0; i < 64; i++) o_pad[i] = k_pad[i] ^ 0x5c;

    /* inner = SHA256(i_pad || message) */
    size_t inner_len = 64 + input->length;
    char* inner_buf = (char*)malloc(inner_len);
    memcpy(inner_buf, i_pad, 64);
    memcpy(inner_buf + 64, input->data, input->length);
    gg_string* inner_str = gg_string_from_buf(inner_buf, (int32_t)inner_len);
    free(inner_buf);
    gg_string* inner_hash = gg_crypto_sha256(inner_str);

    /* Convert inner hash hex to bytes */
    uint8_t inner_bytes[32];
    for (int i = 0; i < 32; i++) {
        unsigned int byte;
        char h[3] = {inner_hash->data[i*2], inner_hash->data[i*2+1], 0};
        sscanf(h, "%02x", &byte);
        inner_bytes[i] = (uint8_t)byte;
    }

    /* outer = SHA256(o_pad || inner_bytes) */
    char outer_buf[96]; /* 64 + 32 */
    memcpy(outer_buf, o_pad, 64);
    memcpy(outer_buf + 64, inner_bytes, 32);
    gg_string* outer_str = gg_string_from_buf(outer_buf, 96);
    return gg_crypto_sha256(outer_str);
}

/* ============================================================
 * BASE64
 * ============================================================ */

static const char gg_b64_table[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

gg_string* gg_base64_encode(gg_string* input) {
    if (!input || input->length == 0) return gg_string_from_cstr("");
    int len = input->length;
    int out_len = 4 * ((len + 2) / 3);
    char* out = (char*)malloc(out_len + 1);
    int j = 0;
    for (int i = 0; i < len; i += 3) {
        uint32_t a = (uint8_t)input->data[i];
        uint32_t b = (i+1 < len) ? (uint8_t)input->data[i+1] : 0;
        uint32_t c = (i+2 < len) ? (uint8_t)input->data[i+2] : 0;
        uint32_t triple = (a << 16) | (b << 8) | c;
        out[j++] = gg_b64_table[(triple >> 18) & 0x3F];
        out[j++] = gg_b64_table[(triple >> 12) & 0x3F];
        out[j++] = (i+1 < len) ? gg_b64_table[(triple >> 6) & 0x3F] : '=';
        out[j++] = (i+2 < len) ? gg_b64_table[triple & 0x3F] : '=';
    }
    out[j] = '\0';
    gg_string* result = gg_string_from_cstr(out);
    free(out);
    return result;
}

static int gg_b64_val(char c) {
    if (c >= 'A' && c <= 'Z') return c - 'A';
    if (c >= 'a' && c <= 'z') return c - 'a' + 26;
    if (c >= '0' && c <= '9') return c - '0' + 52;
    if (c == '+') return 62;
    if (c == '/') return 63;
    return -1;
}

gg_string* gg_base64_decode(gg_string* encoded) {
    if (!encoded || encoded->length == 0) return gg_string_from_cstr("");
    int len = encoded->length;
    int out_len = len * 3 / 4;
    if (len > 0 && encoded->data[len-1] == '=') out_len--;
    if (len > 1 && encoded->data[len-2] == '=') out_len--;

    char* out = (char*)malloc(out_len + 1);
    int j = 0;
    for (int i = 0; i < len; i += 4) {
        int a = gg_b64_val(encoded->data[i]);
        int b = (i+1 < len) ? gg_b64_val(encoded->data[i+1]) : 0;
        int c = (i+2 < len) ? gg_b64_val(encoded->data[i+2]) : 0;
        int d = (i+3 < len) ? gg_b64_val(encoded->data[i+3]) : 0;
        if (a < 0) a = 0;
        if (b < 0) b = 0;
        if (c < 0) c = 0;
        if (d < 0) d = 0;
        uint32_t triple = ((uint32_t)a<<18)|((uint32_t)b<<12)|((uint32_t)c<<6)|(uint32_t)d;
        if (j < out_len) out[j++] = (triple >> 16) & 0xFF;
        if (j < out_len) out[j++] = (triple >> 8) & 0xFF;
        if (j < out_len) out[j++] = triple & 0xFF;
    }
    out[j] = '\0';
    gg_string* result = gg_string_from_buf(out, j);
    free(out);
    return result;
}

/* ============================================================
 * HEX ENCODING
 * ============================================================ */

gg_string* gg_hex_encode(gg_string* input) {
    if (!input || input->length == 0) return gg_string_from_cstr("");
    int len = input->length;
    char* out = (char*)malloc(len * 2 + 1);
    for (int i = 0; i < len; i++)
        snprintf(out + i*2, 3, "%02x", (uint8_t)input->data[i]);
    out[len*2] = '\0';
    gg_string* result = gg_string_from_cstr(out);
    free(out);
    return result;
}

gg_string* gg_hex_decode(gg_string* hexStr) {
    if (!hexStr || hexStr->length < 2) return gg_string_from_cstr("");
    int len = hexStr->length / 2;
    char* out = (char*)malloc(len + 1);
    for (int i = 0; i < len; i++) {
        unsigned int byte;
        char h[3] = {hexStr->data[i*2], hexStr->data[i*2+1], 0};
        sscanf(h, "%02x", &byte);
        out[i] = (char)byte;
    }
    out[len] = '\0';
    gg_string* result = gg_string_from_buf(out, len);
    free(out);
    return result;
}

/* ============================================================
 * XOR CIPHER
 * ============================================================ */

gg_string* gg_xor_encrypt(gg_string* plaintext, gg_string* key) {
    if (!plaintext || !key || key->length == 0) return gg_string_from_cstr("");
    int len = plaintext->length;
    char* buf = (char*)malloc(len);
    for (int i = 0; i < len; i++)
        buf[i] = plaintext->data[i] ^ key->data[i % key->length];
    gg_string* raw = gg_string_from_buf(buf, len);
    free(buf);
    return gg_hex_encode(raw);
}

gg_string* gg_xor_decrypt(gg_string* cipherHex, gg_string* key) {
    if (!cipherHex || !key || key->length == 0) return gg_string_from_cstr("");
    gg_string* raw = gg_hex_decode(cipherHex);
    int len = raw->length;
    char* buf = (char*)malloc(len + 1);
    for (int i = 0; i < len; i++)
        buf[i] = raw->data[i] ^ key->data[i % key->length];
    buf[len] = '\0';
    gg_string* result = gg_string_from_buf(buf, len);
    free(buf);
    return result;
}

/* ============================================================
 * RANDOM
 * ============================================================ */

int32_t gg_random_nextInt(int32_t min, int32_t max) {
    gg_ensure_random_seeded();
    if (max <= min) return min;
    return min + (rand() % (max - min));
}

gg_string* gg_random_nextString(int32_t length) {
    gg_ensure_random_seeded();
    if (length <= 0) return gg_string_from_cstr("");
    static const char charset[] = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    char* buf = (char*)malloc(length + 1);
    for (int32_t i = 0; i < length; i++)
        buf[i] = charset[rand() % (sizeof(charset) - 1)];
    buf[length] = '\0';
    gg_string* result = gg_string_from_cstr(buf);
    free(buf);
    return result;
}

gg_string* gg_random_uuid(void) {
    gg_ensure_random_seeded();
    char buf[37];
    static const char hex[] = "0123456789abcdef";
    for (int i = 0; i < 36; i++) {
        if (i == 8 || i == 13 || i == 18 || i == 23) buf[i] = '-';
        else if (i == 14) buf[i] = '4'; /* version 4 */
        else if (i == 19) buf[i] = hex[(rand() % 4) + 8]; /* variant: 8,9,a,b */
        else buf[i] = hex[rand() % 16];
    }
    buf[36] = '\0';
    return gg_string_from_cstr(buf);
}

/* ============================================================
 * NETWORKING
 * ============================================================ */

void gg_network_init(void) {
#ifdef GG_PLATFORM_WINDOWS
    WSADATA wsa;
    WSAStartup(MAKEWORD(2, 2), &wsa);
#endif
}

void gg_network_shutdown(void) {
#ifdef GG_PLATFORM_WINDOWS
    WSACleanup();
#endif
}

gg_string* gg_network_resolve(gg_string* hostname) {
    if (!hostname) return gg_string_from_cstr("");

    struct addrinfo hints, *res;
    memset(&hints, 0, sizeof(hints));
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_STREAM;

    if (getaddrinfo(hostname->data, NULL, &hints, &res) != 0)
        return gg_string_from_cstr("");

    char ip[INET_ADDRSTRLEN];
    struct sockaddr_in* addr = (struct sockaddr_in*)res->ai_addr;
    inet_ntop(AF_INET, &addr->sin_addr, ip, sizeof(ip));
    freeaddrinfo(res);
    return gg_string_from_cstr(ip);
}

int gg_network_ping(gg_string* host, int32_t port, int32_t timeoutMs) {
    if (!host) return 0;
    gg_string* ip = gg_network_resolve(host);
    if (ip->length == 0) return 0;

#ifdef GG_PLATFORM_WINDOWS
    SOCKET sock = socket(AF_INET, SOCK_STREAM, 0);
    if (sock == INVALID_SOCKET) return 0;

    /* Set timeout */
    DWORD tv = (DWORD)timeoutMs;
    setsockopt(sock, SOL_SOCKET, SO_RCVTIMEO, (const char*)&tv, sizeof(tv));
    setsockopt(sock, SOL_SOCKET, SO_SNDTIMEO, (const char*)&tv, sizeof(tv));
#else
    int sock = socket(AF_INET, SOCK_STREAM, 0);
    if (sock < 0) return 0;

    struct timeval tv;
    tv.tv_sec = timeoutMs / 1000;
    tv.tv_usec = (timeoutMs % 1000) * 1000;
    setsockopt(sock, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
    setsockopt(sock, SOL_SOCKET, SO_SNDTIMEO, &tv, sizeof(tv));
#endif

    struct sockaddr_in addr;
    memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_port = htons((uint16_t)port);
    inet_pton(AF_INET, ip->data, &addr.sin_addr);

    int result = connect(sock, (struct sockaddr*)&addr, sizeof(addr)) == 0;

#ifdef GG_PLATFORM_WINDOWS
    closesocket(sock);
#else
    close(sock);
#endif
    return result;
}

gg_string* gg_network_getHostName(void) {
    char buf[256];
    if (gethostname(buf, sizeof(buf)) == 0)
        return gg_string_from_cstr(buf);
    return gg_string_from_cstr("");
}

/* ============================================================
 * OS UTILITIES
 * ============================================================ */

gg_string* gg_os_platform(void) {
    return gg_string_from_cstr(GG_PLATFORM_NAME);
}

gg_string* gg_os_arch(void) {
#if defined(__x86_64__) || defined(_M_X64)
    return gg_string_from_cstr("x86_64");
#elif defined(__aarch64__) || defined(_M_ARM64)
    return gg_string_from_cstr("aarch64");
#elif defined(__i386__) || defined(_M_IX86)
    return gg_string_from_cstr("i386");
#elif defined(__arm__) || defined(_M_ARM)
    return gg_string_from_cstr("arm");
#else
    return gg_string_from_cstr("unknown");
#endif
}

gg_string* gg_os_getEnv(gg_string* name) {
    if (!name) return gg_string_from_cstr("");
    const char* val = getenv(name->data);
    return gg_string_from_cstr(val ? val : "");
}

int gg_os_setEnv(gg_string* name, gg_string* value) {
    if (!name || !value) return 0;
#ifdef GG_PLATFORM_WINDOWS
    return _putenv_s(name->data, value->data) == 0;
#else
    return setenv(name->data, value->data, 1) == 0;
#endif
}

int gg_os_removeEnv(gg_string* name) {
    if (!name) return 0;
#ifdef GG_PLATFORM_WINDOWS
    return _putenv_s(name->data, "") == 0;
#else
    return unsetenv(name->data) == 0;
#endif
}

void gg_os_exit(int32_t code) {
    exit(code);
}

int32_t gg_os_time(void) {
    return (int32_t)time(NULL);
}

void gg_os_sleep(int32_t ms) {
#ifdef GG_PLATFORM_WINDOWS
    Sleep((DWORD)ms);
#else
    usleep((useconds_t)ms * 1000);
#endif
}

int32_t gg_os_cpuCount(void) {
#ifdef GG_PLATFORM_WINDOWS
    SYSTEM_INFO info;
    GetSystemInfo(&info);
    return (int32_t)info.dwNumberOfProcessors;
#else
    long n = sysconf(_SC_NPROCESSORS_ONLN);
    return n > 0 ? (int32_t)n : 1;
#endif
}

gg_string* gg_os_userName(void) {
#ifdef GG_PLATFORM_WINDOWS
    char buf[256];
    DWORD size = sizeof(buf);
    if (GetUserNameA(buf, &size)) return gg_string_from_cstr(buf);
    return gg_string_from_cstr("");
#else
    char* name = getlogin();
    return gg_string_from_cstr(name ? name : "");
#endif
}

gg_string* gg_os_homeDir(void) {
#ifdef GG_PLATFORM_WINDOWS
    const char* home = getenv("USERPROFILE");
#else
    const char* home = getenv("HOME");
#endif
    return gg_string_from_cstr(home ? home : "");
}

gg_string* gg_os_tempDir(void) {
#ifdef GG_PLATFORM_WINDOWS
    char buf[MAX_PATH];
    if (GetTempPathA(sizeof(buf), buf)) return gg_string_from_cstr(buf);
    return gg_string_from_cstr("C:\\Temp");
#else
    const char* tmp = getenv("TMPDIR");
    return gg_string_from_cstr(tmp ? tmp : "/tmp");
#endif
}

gg_string* gg_os_pathSeparator(void) {
    return gg_string_from_cstr(GG_PATH_SEP);
}

gg_string* gg_os_lineEnding(void) {
    return gg_string_from_cstr(GG_LINE_END);
}

/* ============================================================
 * PROCESS
 * ============================================================ */

gg_string* gg_process_exec(gg_string* command) {
    if (!command) return gg_string_from_cstr("");
#ifdef GG_PLATFORM_WINDOWS
    FILE* fp = _popen(command->data, "r");
#else
    FILE* fp = popen(command->data, "r");
#endif
    if (!fp) return gg_string_from_cstr("");

    char buf[4096];
    size_t total = 0;
    size_t capacity = 4096;
    char* output = (char*)malloc(capacity);
    output[0] = '\0';

    while (fgets(buf, sizeof(buf), fp)) {
        size_t len = strlen(buf);
        if (total + len >= capacity) {
            capacity *= 2;
            output = (char*)realloc(output, capacity);
        }
        memcpy(output + total, buf, len);
        total += len;
    }
    output[total] = '\0';

#ifdef GG_PLATFORM_WINDOWS
    _pclose(fp);
#else
    pclose(fp);
#endif

    gg_string* result = gg_string_from_buf(output, (int32_t)total);
    free(output);
    return result;
}

int32_t gg_process_run(gg_string* command) {
    if (!command) return -1;
    return (int32_t)system(command->data);
}

int32_t gg_process_pid(void) {
#ifdef GG_PLATFORM_WINDOWS
    return (int32_t)GetCurrentProcessId();
#else
    return (int32_t)getpid();
#endif
}

/* ============================================================
 * CLOCK
 * ============================================================ */

int32_t gg_clock_now(void) {
#ifdef GG_PLATFORM_WINDOWS
    return (int32_t)(GetTickCount64() & 0x7FFFFFFF);
#else
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int32_t)((ts.tv_sec * 1000) + (ts.tv_nsec / 1000000));
#endif
}

gg_string* gg_clock_date(void) {
    time_t t = time(NULL);
    struct tm* lt = localtime(&t);
    char buf[11];
    strftime(buf, sizeof(buf), "%Y-%m-%d", lt);
    return gg_string_from_cstr(buf);
}

gg_string* gg_clock_time(void) {
    time_t t = time(NULL);
    struct tm* lt = localtime(&t);
    char buf[9];
    strftime(buf, sizeof(buf), "%H:%M:%S", lt);
    return gg_string_from_cstr(buf);
}

gg_string* gg_clock_dateTime(void) {
    time_t t = time(NULL);
    struct tm* lt = localtime(&t);
    char buf[20];
    strftime(buf, sizeof(buf), "%Y-%m-%d %H:%M:%S", lt);
    return gg_string_from_cstr(buf);
}

/* ============================================================
 * EXTENSION METHODS — Type conversions & utilities
 * ============================================================ */

/* --- toString extensions --- */

const char* gg_ext_int_toString(int value) {
    char* buf = (char*)malloc(32);
    snprintf(buf, 32, "%d", value);
    return buf;
}

const char* gg_ext_long_toString(long long value) {
    char* buf = (char*)malloc(32);
    snprintf(buf, 32, "%lld", value);
    return buf;
}

const char* gg_ext_double_toString(double value) {
    char* buf = (char*)malloc(64);
    snprintf(buf, 64, "%g", value);
    return buf;
}

const char* gg_ext_float_toString(float value) {
    char* buf = (char*)malloc(64);
    snprintf(buf, 64, "%g", (double)value);
    return buf;
}

const char* gg_ext_bool_toString(int value) {
    return value ? "true" : "false";
}

const char* gg_ext_char_toString(char value) {
    char* buf = (char*)malloc(2);
    buf[0] = value;
    buf[1] = '\0';
    return buf;
}

/* --- toInt extensions --- */

int gg_ext_double_toInt(double value) { return (int)value; }
int gg_ext_float_toInt(float value) { return (int)value; }
int gg_ext_string_toInt(const char* value) { return value ? atoi(value) : 0; }
int gg_ext_long_toInt(long long value) { return (int)value; }
int gg_ext_bool_toInt(int value) { return value ? 1 : 0; }
int gg_ext_char_toInt(char value) { return (int)value; }

/* --- toLong extensions --- */

long long gg_ext_int_toLong(int value) { return (long long)value; }
long long gg_ext_double_toLong(double value) { return (long long)value; }
long long gg_ext_string_toLong(const char* value) { return value ? atoll(value) : 0; }

/* --- toDouble / toDecimal extensions --- */

double gg_ext_int_toDouble(int value) { return (double)value; }
double gg_ext_long_toDouble(long long value) { return (double)value; }
double gg_ext_float_toDouble(float value) { return (double)value; }
double gg_ext_string_toDouble(const char* value) { return value ? atof(value) : 0.0; }

/* --- toFloat extensions --- */

float gg_ext_int_toFloat(int value) { return (float)value; }
float gg_ext_double_toFloat(double value) { return (float)value; }
float gg_ext_string_toFloat(const char* value) { return value ? (float)atof(value) : 0.0f; }

/* --- toBool extensions --- */

int gg_ext_int_toBool(int value) { return value != 0; }
int gg_ext_string_toBool(const char* value) {
    if (!value) return 0;
    return (strcmp(value, "true") == 0 || strcmp(value, "1") == 0 ||
            strcmp(value, "yes") == 0 || strcmp(value, "True") == 0);
}
int gg_ext_double_toBool(double value) { return value != 0.0; }

/* --- toChar extensions --- */

char gg_ext_int_toChar(int value) { return (char)value; }
char gg_ext_string_toChar(const char* value) { return (value && value[0]) ? value[0] : '\0'; }

/* --- round / ceil / floor on numeric types --- */

double gg_ext_double_round(double value, int decimals) {
    double factor = 1.0;
    for (int i = 0; i < decimals; i++) factor *= 10.0;
    return round(value * factor) / factor;
}

double gg_ext_float_round(float value, int decimals) {
    return gg_ext_double_round((double)value, decimals);
}

int gg_ext_double_roundToInt(double value) { return (int)round(value); }
int gg_ext_float_roundToInt(float value) { return (int)roundf(value); }
double gg_ext_double_ceil(double value) { return ceil(value); }
double gg_ext_double_floor(double value) { return floor(value); }

/* --- abs on numeric types --- */

int gg_ext_int_abs(int value) { return value < 0 ? -value : value; }
long long gg_ext_long_abs(long long value) { return value < 0 ? -value : value; }
double gg_ext_double_abs(double value) { return fabs(value); }
float gg_ext_float_abs(float value) { return fabsf(value); }

/* --- clamp --- */

int gg_ext_int_clamp(int value, int min_val, int max_val) {
    if (value < min_val) return min_val;
    if (value > max_val) return max_val;
    return value;
}

double gg_ext_double_clamp(double value, double min_val, double max_val) {
    if (value < min_val) return min_val;
    if (value > max_val) return max_val;
    return value;
}

/* --- string query extensions --- */

int gg_ext_string_length(const char* value) {
    return value ? (int)strlen(value) : 0;
}

int gg_ext_string_isEmpty(const char* value) {
    return (!value || value[0] == '\0') ? 1 : 0;
}

const char* gg_ext_string_toUpper(const char* value) {
    if (!value) return "";
    int len = (int)strlen(value);
    char* buf = (char*)malloc(len + 1);
    for (int i = 0; i < len; i++) buf[i] = (char)toupper((unsigned char)value[i]);
    buf[len] = '\0';
    return buf;
}

const char* gg_ext_string_toLower(const char* value) {
    if (!value) return "";
    int len = (int)strlen(value);
    char* buf = (char*)malloc(len + 1);
    for (int i = 0; i < len; i++) buf[i] = (char)tolower((unsigned char)value[i]);
    buf[len] = '\0';
    return buf;
}

const char* gg_ext_string_trim(const char* value) {
    if (!value) return "";
    int len = (int)strlen(value);
    int start = 0, end = len - 1;
    while (start <= end && isspace((unsigned char)value[start])) start++;
    while (end >= start && isspace((unsigned char)value[end])) end--;
    int new_len = end - start + 1;
    if (new_len <= 0) return "";
    char* buf = (char*)malloc(new_len + 1);
    memcpy(buf, value + start, new_len);
    buf[new_len] = '\0';
    return buf;
}

const char* gg_ext_string_substring(const char* value, int start, int length) {
    if (!value) return "";
    int slen = (int)strlen(value);
    if (start < 0 || start >= slen) return "";
    if (start + length > slen) length = slen - start;
    char* buf = (char*)malloc(length + 1);
    memcpy(buf, value + start, length);
    buf[length] = '\0';
    return buf;
}

int gg_ext_string_contains(const char* value, const char* sub) {
    if (!value || !sub) return 0;
    return strstr(value, sub) != NULL;
}

int gg_ext_string_startsWith(const char* value, const char* prefix) {
    if (!value || !prefix) return 0;
    size_t plen = strlen(prefix);
    return strncmp(value, prefix, plen) == 0;
}

int gg_ext_string_endsWith(const char* value, const char* suffix) {
    if (!value || !suffix) return 0;
    size_t vlen = strlen(value);
    size_t slen = strlen(suffix);
    if (slen > vlen) return 0;
    return strcmp(value + vlen - slen, suffix) == 0;
}

int gg_ext_string_indexOf(const char* value, const char* sub) {
    if (!value || !sub) return -1;
    char* found = strstr(value, sub);
    return found ? (int)(found - value) : -1;
}

const char* gg_ext_string_replace(const char* value, const char* old_str, const char* new_str) {
    if (!value || !old_str || old_str[0] == '\0') return value ? value : "";
    if (!new_str) new_str = "";
    int old_len = (int)strlen(old_str);
    int new_len = (int)strlen(new_str);

    /* Count occurrences */
    int count = 0;
    const char* pos = value;
    while ((pos = strstr(pos, old_str)) != NULL) { count++; pos += old_len; }
    if (count == 0) return value;

    int val_len = (int)strlen(value);
    int result_len = val_len + count * (new_len - old_len);
    char* buf = (char*)malloc(result_len + 1);
    char* dest = buf;
    pos = value;
    while (*pos) {
        const char* found = strstr(pos, old_str);
        if (found) {
            int before = (int)(found - pos);
            memcpy(dest, pos, before);
            dest += before;
            memcpy(dest, new_str, new_len);
            dest += new_len;
            pos = found + old_len;
        } else {
            strcpy(dest, pos);
            break;
        }
    }
    buf[result_len] = '\0';
    return buf;
}

const char* gg_ext_string_charAt(const char* value, int index) {
    if (!value || index < 0 || index >= (int)strlen(value)) return "";
    char* buf = (char*)malloc(2);
    buf[0] = value[index];
    buf[1] = '\0';
    return buf;
}

const char* gg_ext_string_reverse(const char* value) {
    if (!value) return "";
    int len = (int)strlen(value);
    char* buf = (char*)malloc(len + 1);
    for (int i = 0; i < len; i++) buf[i] = value[len - 1 - i];
    buf[len] = '\0';
    return buf;
}

const char* gg_ext_string_padLeft(const char* value, int totalWidth, char padChar) {
    if (!value) value = "";
    int len = (int)strlen(value);
    if (len >= totalWidth) return value;
    int pad = totalWidth - len;
    char* buf = (char*)malloc(totalWidth + 1);
    memset(buf, padChar, pad);
    memcpy(buf + pad, value, len);
    buf[totalWidth] = '\0';
    return buf;
}

const char* gg_ext_string_padRight(const char* value, int totalWidth, char padChar) {
    if (!value) value = "";
    int len = (int)strlen(value);
    if (len >= totalWidth) return value;
    char* buf = (char*)malloc(totalWidth + 1);
    memcpy(buf, value, len);
    memset(buf + len, padChar, totalWidth - len);
    buf[totalWidth] = '\0';
    return buf;
}

/* ============================================================
 * HASH MAP — Open addressing with Robin Hood hashing
 * Performance-first: power-of-2 capacity, FNV-1a hash, linear probing
 * ============================================================ */

static uint32_t gg_hash_fnv1a(const char* key) {
    uint32_t hash = 2166136261u;
    for (const char* p = key; *p; p++) {
        hash ^= (uint8_t)*p;
        hash *= 16777619u;
    }
    return hash;
}

static int32_t gg_next_power_of_2(int32_t n) {
    n--;
    n |= n >> 1; n |= n >> 2; n |= n >> 4;
    n |= n >> 8; n |= n >> 16;
    return n + 1;
}

gg_hashmap* gg_hashmap_new(int32_t elem_size) {
    return gg_hashmap_new_capacity(elem_size, 16);
}

gg_hashmap* gg_hashmap_new_capacity(int32_t elem_size, int32_t initial_capacity) {
    gg_hashmap* map = (gg_hashmap*)calloc(1, sizeof(gg_hashmap));
    map->capacity = gg_next_power_of_2(initial_capacity < 16 ? 16 : initial_capacity);
    map->count = 0;
    map->elem_size = elem_size;
    map->load_factor = 0.75f;
    map->buckets = (gg_hashmap_entry*)calloc(map->capacity, sizeof(gg_hashmap_entry));
    return map;
}

static void gg_hashmap_resize(gg_hashmap* map) {
    int32_t old_cap = map->capacity;
    gg_hashmap_entry* old_buckets = map->buckets;

    map->capacity *= 2;
    map->buckets = (gg_hashmap_entry*)calloc(map->capacity, sizeof(gg_hashmap_entry));
    map->count = 0;

    for (int32_t i = 0; i < old_cap; i++) {
        if (old_buckets[i].occupied && !old_buckets[i].deleted) {
            gg_hashmap_put(map, old_buckets[i].key, old_buckets[i].value);
        }
    }

    for (int32_t i = 0; i < old_cap; i++) {
        if (old_buckets[i].value && old_buckets[i].deleted) {
            free(old_buckets[i].value);
        }
    }
    free(old_buckets);
}

void gg_hashmap_put(gg_hashmap* map, const char* key, void* value) {
    if (!map || !key) return;

    if ((float)(map->count + 1) / map->capacity > map->load_factor) {
        gg_hashmap_resize(map);
    }

    uint32_t hash = gg_hash_fnv1a(key);
    int32_t mask = map->capacity - 1;
    int32_t idx = (int32_t)(hash & (uint32_t)mask);

    for (int32_t i = 0; i < map->capacity; i++) {
        int32_t probe = (idx + i) & mask;
        gg_hashmap_entry* e = &map->buckets[probe];

        if (!e->occupied || e->deleted) {
            if (!e->occupied) {
                e->key = strdup(key);
            } else {
                free((void*)e->key);
                e->key = strdup(key);
            }
            e->value = malloc(map->elem_size);
            memcpy(e->value, value, map->elem_size);
            e->occupied = 1;
            e->deleted = 0;
            map->count++;
            return;
        }

        if (strcmp(e->key, key) == 0) {
            memcpy(e->value, value, map->elem_size);
            return;
        }
    }
}

void* gg_hashmap_get(gg_hashmap* map, const char* key) {
    if (!map || !key) return NULL;

    uint32_t hash = gg_hash_fnv1a(key);
    int32_t mask = map->capacity - 1;
    int32_t idx = (int32_t)(hash & (uint32_t)mask);

    for (int32_t i = 0; i < map->capacity; i++) {
        int32_t probe = (idx + i) & mask;
        gg_hashmap_entry* e = &map->buckets[probe];

        if (!e->occupied) return NULL;
        if (!e->deleted && strcmp(e->key, key) == 0) return e->value;
    }
    return NULL;
}

int gg_hashmap_containsKey(gg_hashmap* map, const char* key) {
    return gg_hashmap_get(map, key) != NULL;
}

int gg_hashmap_remove(gg_hashmap* map, const char* key) {
    if (!map || !key) return 0;

    uint32_t hash = gg_hash_fnv1a(key);
    int32_t mask = map->capacity - 1;
    int32_t idx = (int32_t)(hash & (uint32_t)mask);

    for (int32_t i = 0; i < map->capacity; i++) {
        int32_t probe = (idx + i) & mask;
        gg_hashmap_entry* e = &map->buckets[probe];

        if (!e->occupied) return 0;
        if (!e->deleted && strcmp(e->key, key) == 0) {
            e->deleted = 1;
            free(e->value);
            e->value = NULL;
            map->count--;
            return 1;
        }
    }
    return 0;
}

int32_t gg_hashmap_count(gg_hashmap* map) { return map ? map->count : 0; }

void gg_hashmap_clear(gg_hashmap* map) {
    if (!map) return;
    for (int32_t i = 0; i < map->capacity; i++) {
        if (map->buckets[i].occupied) {
            free((void*)map->buckets[i].key);
            free(map->buckets[i].value);
            map->buckets[i].occupied = 0;
            map->buckets[i].deleted = 0;
        }
    }
    map->count = 0;
}

void gg_hashmap_free(gg_hashmap* map) {
    if (!map) return;
    gg_hashmap_clear(map);
    free(map->buckets);
    free(map);
}

/* ============================================================
 * HASH SET — Open addressing
 * ============================================================ */

gg_hashset* gg_hashset_new(void) { return gg_hashset_new_capacity(16); }

gg_hashset* gg_hashset_new_capacity(int32_t initial_capacity) {
    gg_hashset* set = (gg_hashset*)calloc(1, sizeof(gg_hashset));
    set->capacity = gg_next_power_of_2(initial_capacity < 16 ? 16 : initial_capacity);
    set->count = 0;
    set->load_factor = 0.75f;
    set->buckets = (gg_hashset_entry*)calloc(set->capacity, sizeof(gg_hashset_entry));
    return set;
}

static void gg_hashset_resize(gg_hashset* set) {
    int32_t old_cap = set->capacity;
    gg_hashset_entry* old_buckets = set->buckets;

    set->capacity *= 2;
    set->buckets = (gg_hashset_entry*)calloc(set->capacity, sizeof(gg_hashset_entry));
    set->count = 0;

    for (int32_t i = 0; i < old_cap; i++) {
        if (old_buckets[i].occupied && !old_buckets[i].deleted) {
            gg_hashset_add(set, old_buckets[i].key);
        }
    }
    free(old_buckets);
}

int gg_hashset_add(gg_hashset* set, const char* key) {
    if (!set || !key) return 0;
    if ((float)(set->count + 1) / set->capacity > set->load_factor) {
        gg_hashset_resize(set);
    }

    uint32_t hash = gg_hash_fnv1a(key);
    int32_t mask = set->capacity - 1;
    int32_t idx = (int32_t)(hash & (uint32_t)mask);

    for (int32_t i = 0; i < set->capacity; i++) {
        int32_t probe = (idx + i) & mask;
        gg_hashset_entry* e = &set->buckets[probe];

        if (!e->occupied || e->deleted) {
            e->key = strdup(key);
            e->occupied = 1;
            e->deleted = 0;
            set->count++;
            return 1;
        }
        if (strcmp(e->key, key) == 0) return 0;
    }
    return 0;
}

int gg_hashset_contains(gg_hashset* set, const char* key) {
    if (!set || !key) return 0;
    uint32_t hash = gg_hash_fnv1a(key);
    int32_t mask = set->capacity - 1;
    int32_t idx = (int32_t)(hash & (uint32_t)mask);

    for (int32_t i = 0; i < set->capacity; i++) {
        int32_t probe = (idx + i) & mask;
        gg_hashset_entry* e = &set->buckets[probe];
        if (!e->occupied) return 0;
        if (!e->deleted && strcmp(e->key, key) == 0) return 1;
    }
    return 0;
}

int gg_hashset_remove(gg_hashset* set, const char* key) {
    if (!set || !key) return 0;
    uint32_t hash = gg_hash_fnv1a(key);
    int32_t mask = set->capacity - 1;
    int32_t idx = (int32_t)(hash & (uint32_t)mask);

    for (int32_t i = 0; i < set->capacity; i++) {
        int32_t probe = (idx + i) & mask;
        gg_hashset_entry* e = &set->buckets[probe];
        if (!e->occupied) return 0;
        if (!e->deleted && strcmp(e->key, key) == 0) {
            e->deleted = 1;
            free((void*)e->key);
            e->key = NULL;
            set->count--;
            return 1;
        }
    }
    return 0;
}

int32_t gg_hashset_count(gg_hashset* set) { return set ? set->count : 0; }

void gg_hashset_clear(gg_hashset* set) {
    if (!set) return;
    for (int32_t i = 0; i < set->capacity; i++) {
        if (set->buckets[i].occupied) {
            free((void*)set->buckets[i].key);
            set->buckets[i].occupied = 0;
            set->buckets[i].deleted = 0;
        }
    }
    set->count = 0;
}

void gg_hashset_free(gg_hashset* set) {
    if (!set) return;
    gg_hashset_clear(set);
    free(set->buckets);
    free(set);
}

/* ============================================================
 * LINKED LIST — Doubly-linked
 * ============================================================ */

gg_list* gg_list_new(int32_t elem_size) {
    gg_list* list = (gg_list*)calloc(1, sizeof(gg_list));
    list->elem_size = elem_size;
    return list;
}

static gg_list_node* gg_list_node_new(int32_t elem_size, void* data) {
    gg_list_node* node = (gg_list_node*)calloc(1, sizeof(gg_list_node));
    node->data = malloc(elem_size);
    memcpy(node->data, data, elem_size);
    return node;
}

void gg_list_addFirst(gg_list* list, void* data) {
    if (!list) return;
    gg_list_node* node = gg_list_node_new(list->elem_size, data);
    node->next = list->head;
    if (list->head) list->head->prev = node;
    list->head = node;
    if (!list->tail) list->tail = node;
    list->count++;
}

void gg_list_addLast(gg_list* list, void* data) {
    if (!list) return;
    gg_list_node* node = gg_list_node_new(list->elem_size, data);
    node->prev = list->tail;
    if (list->tail) list->tail->next = node;
    list->tail = node;
    if (!list->head) list->head = node;
    list->count++;
}

void* gg_list_getFirst(gg_list* list) {
    return (list && list->head) ? list->head->data : NULL;
}

void* gg_list_getLast(gg_list* list) {
    return (list && list->tail) ? list->tail->data : NULL;
}

void* gg_list_get(gg_list* list, int32_t index) {
    if (!list || index < 0 || index >= list->count) return NULL;
    gg_list_node* node = list->head;
    for (int32_t i = 0; i < index; i++) node = node->next;
    return node->data;
}

int gg_list_removeFirst(gg_list* list) {
    if (!list || !list->head) return 0;
    gg_list_node* old = list->head;
    list->head = old->next;
    if (list->head) list->head->prev = NULL;
    else list->tail = NULL;
    free(old->data);
    free(old);
    list->count--;
    return 1;
}

int gg_list_removeLast(gg_list* list) {
    if (!list || !list->tail) return 0;
    gg_list_node* old = list->tail;
    list->tail = old->prev;
    if (list->tail) list->tail->next = NULL;
    else list->head = NULL;
    free(old->data);
    free(old);
    list->count--;
    return 1;
}

int32_t gg_list_count(gg_list* list) { return list ? list->count : 0; }

void gg_list_clear(gg_list* list) {
    if (!list) return;
    gg_list_node* node = list->head;
    while (node) {
        gg_list_node* next = node->next;
        free(node->data);
        free(node);
        node = next;
    }
    list->head = list->tail = NULL;
    list->count = 0;
}

void gg_list_free(gg_list* list) {
    if (!list) return;
    gg_list_clear(list);
    free(list);
}

/* ============================================================
 * STACK — LIFO using dynamic array
 * ============================================================ */

gg_stack* gg_stack_new(int32_t elem_size) {
    gg_stack* s = (gg_stack*)calloc(1, sizeof(gg_stack));
    s->elem_size = elem_size;
    s->capacity = 16;
    s->data = malloc(s->capacity * elem_size);
    return s;
}

void gg_stack_push(gg_stack* stack, void* value) {
    if (!stack) return;
    if (stack->count >= stack->capacity) {
        stack->capacity *= 2;
        stack->data = realloc(stack->data, stack->capacity * stack->elem_size);
    }
    memcpy((char*)stack->data + stack->count * stack->elem_size, value, stack->elem_size);
    stack->count++;
}

void* gg_stack_peek(gg_stack* stack) {
    if (!stack || stack->count == 0) return NULL;
    return (char*)stack->data + (stack->count - 1) * stack->elem_size;
}

int gg_stack_pop(gg_stack* stack, void* out_value) {
    if (!stack || stack->count == 0) return 0;
    stack->count--;
    if (out_value) {
        memcpy(out_value, (char*)stack->data + stack->count * stack->elem_size, stack->elem_size);
    }
    return 1;
}

int32_t gg_stack_count(gg_stack* stack) { return stack ? stack->count : 0; }
int gg_stack_isEmpty(gg_stack* stack) { return !stack || stack->count == 0; }

void gg_stack_clear(gg_stack* stack) {
    if (stack) stack->count = 0;
}

void gg_stack_free(gg_stack* stack) {
    if (!stack) return;
    free(stack->data);
    free(stack);
}

/* ============================================================
 * QUEUE — FIFO using circular buffer
 * ============================================================ */

gg_queue* gg_queue_new(int32_t elem_size) {
    gg_queue* q = (gg_queue*)calloc(1, sizeof(gg_queue));
    q->elem_size = elem_size;
    q->capacity = 16;
    q->data = malloc(q->capacity * elem_size);
    return q;
}

static void gg_queue_resize(gg_queue* q) {
    int32_t new_cap = q->capacity * 2;
    void* new_data = malloc(new_cap * q->elem_size);
    for (int32_t i = 0; i < q->count; i++) {
        int32_t src = (q->head + i) % q->capacity;
        memcpy((char*)new_data + i * q->elem_size,
               (char*)q->data + src * q->elem_size,
               q->elem_size);
    }
    free(q->data);
    q->data = new_data;
    q->head = 0;
    q->tail = q->count;
    q->capacity = new_cap;
}

void gg_queue_enqueue(gg_queue* queue, void* value) {
    if (!queue) return;
    if (queue->count >= queue->capacity) gg_queue_resize(queue);
    memcpy((char*)queue->data + queue->tail * queue->elem_size, value, queue->elem_size);
    queue->tail = (queue->tail + 1) % queue->capacity;
    queue->count++;
}

int gg_queue_dequeue(gg_queue* queue, void* out_value) {
    if (!queue || queue->count == 0) return 0;
    if (out_value) {
        memcpy(out_value, (char*)queue->data + queue->head * queue->elem_size, queue->elem_size);
    }
    queue->head = (queue->head + 1) % queue->capacity;
    queue->count--;
    return 1;
}

void* gg_queue_peek(gg_queue* queue) {
    if (!queue || queue->count == 0) return NULL;
    return (char*)queue->data + queue->head * queue->elem_size;
}

int32_t gg_queue_count(gg_queue* queue) { return queue ? queue->count : 0; }
int gg_queue_isEmpty(gg_queue* queue) { return !queue || queue->count == 0; }

void gg_queue_clear(gg_queue* queue) {
    if (!queue) return;
    queue->head = queue->tail = queue->count = 0;
}

void gg_queue_free(gg_queue* queue) {
    if (!queue) return;
    free(queue->data);
    free(queue);
}

/* ============================================================
 * ENTRY POINT
 * ============================================================ */

/**
 * Program entry point.
 * Initializes the GC, calls Program_main(), then cleans up.
 */
int main(int argc, char** argv) {
    (void)argc;
    (void)argv;

    /* Initialize the garbage collector. */
    gg_gc_init();

    /* Initialize network subsystem (Winsock on Windows). */
    gg_network_init();

    /* Call the ggLang program's main. */
    Program_main();

    /* Shutdown network subsystem. */
    gg_network_shutdown();

    /* Final GC pass — free all remaining objects. */
    gg_gc_shutdown();

    return 0;
}
