/*
 * ggLang Runtime Library - Header
 * Native runtime library for programs compiled with ggLang.
 *
 * Provides:
 *  - Garbage collector (mark-and-sweep)
 *  - Memory management (GC-tracked allocation)
 *  - String type (gg_string)
 *  - Dynamic array type (gg_array)
 *  - Console I/O
 *  - Math functions
 *  - Type conversions
 *  - File I/O (Files, Directory, Path)
 *  - Cryptography (SHA-256, MD5, SHA-1, CRC32, Base64, Hex, XOR, HMAC, Random)
 *  - Networking (TCP, UDP, DNS, URL)
 *  - OS utilities (environment, process, clock, platform)
 *
 * Supports: Linux, macOS, Windows (via #ifdef _WIN32)
 */

#ifndef GG_RUNTIME_H
#define GG_RUNTIME_H

/* ============================================================
 * PLATFORM DETECTION
 * ============================================================ */

#ifdef _WIN32
    #define GG_PLATFORM_WINDOWS 1
    #define GG_PLATFORM_NAME "windows"
    #ifndef _CRT_SECURE_NO_WARNINGS
        #define _CRT_SECURE_NO_WARNINGS
    #endif
    #include <windows.h>
    #include <winsock2.h>
    #include <ws2tcpip.h>
    #include <io.h>
    #include <direct.h>
    #include <process.h>
    #pragma comment(lib, "ws2_32.lib")
    #define GG_PATH_SEP "\\"
    #define GG_LINE_END "\r\n"
    typedef int socklen_t;
#elif defined(__APPLE__)
    #define GG_PLATFORM_MACOS 1
    #define GG_PLATFORM_NAME "macos"
    #include <unistd.h>
    #include <sys/stat.h>
    #include <sys/types.h>
    #include <sys/socket.h>
    #include <sys/time.h>
    #include <netinet/in.h>
    #include <arpa/inet.h>
    #include <netdb.h>
    #include <dirent.h>
    #include <errno.h>
    #define GG_PATH_SEP "/"
    #define GG_LINE_END "\n"
#else
    #define GG_PLATFORM_LINUX 1
    #define GG_PLATFORM_NAME "linux"
    #include <unistd.h>
    #include <sys/stat.h>
    #include <sys/types.h>
    #include <sys/socket.h>
    #include <sys/time.h>
    #include <netinet/in.h>
    #include <arpa/inet.h>
    #include <netdb.h>
    #include <dirent.h>
    #include <errno.h>
    #define GG_PATH_SEP "/"
    #define GG_LINE_END "\n"
#endif

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <math.h>
#include <stdbool.h>
#include <time.h>

/* ============================================================
 * GARBAGE COLLECTOR (Mark-and-Sweep)
 * ============================================================ */

/*
 * When GG_NO_GC is defined (via compiler flag), the garbage collector
 * is completely disabled. Memory is managed manually:
 *   - gg_alloc() uses raw calloc()
 *   - gg_free() uses raw free()
 *   - Memory.free(obj) is available in ggLang code
 *   - GC init/shutdown/collect are no-ops
 * This mode is intended for embedded systems or when the user wants
 * full control over memory allocation and deallocation.
 */

#ifdef GG_NO_GC

/* No-op stubs when GC is disabled */
#define gg_gc_init()          ((void)0)
#define gg_gc_shutdown()      ((void)0)
#define gg_gc_collect()       ((void)0)
#define gg_gc_add_root(p)     ((void)0)
#define gg_gc_remove_root(p)  ((void)0)
#define gg_gc_set_memory_limit(l) ((void)0)
#define gg_gc_push_root_frame() (0)
#define gg_gc_pop_root_frame(frame) ((void)0)
#define gg_gc_write_barrier(slot, val) (*(void**)(slot) = (void*)(val))

static inline void* gg_gc_alloc_nogc(size_t size) {
    void* ptr = calloc(1, size);
    if (!ptr) {
        fprintf(stderr, "[ggLang] Fatal error: out of memory (%zu bytes)\n", size);
        exit(1);
    }
    return ptr;
}
#define gg_gc_alloc(s)  gg_gc_alloc_nogc(s)
#define gg_alloc(s)     gg_gc_alloc_nogc(s)
#define gg_free(p)      free(p)
#define gg_retain(p)    ((void)0)
#define gg_release(p)   ((void)0)

/** Manual memory free — callable from ggLang as Memory.free(obj) */
static inline void Memory_free(void* ptr) { free(ptr); }

/** Manual memory alloc — callable from ggLang as Memory.alloc(size) */
static inline void* Memory_alloc(size_t size) { return gg_gc_alloc_nogc(size); }

#else /* GG_NO_GC not defined — full GC mode */

/**
 * Header prepended to every GC-managed allocation.
 * Forms an intrusive linked list of all live objects.
 */
typedef struct gg_gc_header {
    struct gg_gc_header* next;   /* Next object in the GC heap list */
    size_t size;                 /* Allocation size (excluding header) */
    uint8_t marked;              /* Mark bit for mark-and-sweep */
} gg_gc_header;

/** Default allocation threshold before triggering collection. */
#define GG_GC_INITIAL_THRESHOLD 1024

/** Maximum number of GC roots that can be registered simultaneously. */
#define GG_GC_MAX_ROOTS 4096

/**
 * Global GC state.
 */
typedef struct {
    gg_gc_header* heap;          /* Head of the linked list of all objects */
    void* roots[GG_GC_MAX_ROOTS]; /* Root set — pointers the GC scans from */
    int root_count;              /* Number of active roots */
    size_t alloc_count;          /* Allocations since last collection */
    size_t threshold;            /* Allocation count to trigger collection */
    size_t total_allocated;      /* Total bytes currently allocated */
    size_t total_collected;      /* Cumulative bytes freed by GC */
    size_t collections;          /* Number of GC cycles performed */
    size_t memory_limit;         /* Maximum memory limit in bytes (0 = unlimited) */
} gg_gc_state;

/** Initializes the garbage collector. Must be called before any allocation. */
void gg_gc_init(void);

/** Shuts down the GC, freeing all remaining objects. */
void gg_gc_shutdown(void);

/**
 * Allocates GC-tracked memory.
 * The returned pointer is tracked by the collector and will be freed
 * automatically when no longer reachable from a root.
 */
void* gg_gc_alloc(size_t size);

/**
 * Registers a pointer as a GC root.
 * Roots are starting points for the mark phase; any object reachable
 * from a root is considered alive.
 * @param root_ptr Pointer to a variable holding a GC-managed pointer.
 */
void gg_gc_add_root(void* root_ptr);

/**
 * Removes a previously registered root.
 */
void gg_gc_remove_root(void* root_ptr);

/**
 * Triggers a full mark-and-sweep collection cycle.
 * Typically called automatically when alloc_count exceeds threshold,
 * but can also be invoked manually.
 */
void gg_gc_collect(void);

/**
 * Returns a snapshot of current GC statistics.
 */
gg_gc_state* gg_gc_get_state(void);

/**
 * Sets the maximum memory limit in bytes.
 * When exceeded, the GC will force a collection. If memory is still
 * above the limit after collection, the program terminates.
 * Set to 0 to disable the memory limit (default).
 * Designed for embedded/constrained environments.
 */
void gg_gc_set_memory_limit(size_t limit_bytes);

/**
 * Starts a root frame and returns a snapshot token.
 * Use with gg_gc_pop_root_frame() to unwind temporary roots.
 */
int gg_gc_push_root_frame(void);

/**
 * Restores the root stack to a previous frame snapshot.
 */
void gg_gc_pop_root_frame(int frame);

/**
 * Write barrier hook used by generated assignments to reference slots.
 * Current implementation is a passthrough assignment and is reserved
 * for future incremental/generational GC support.
 */
void gg_gc_write_barrier(void** slot, void* new_value);

/* ============================================================
 * MEMORY MANAGEMENT (Legacy API — delegates to GC)
 * ============================================================ */

/**
 * Allocates memory tracked by the garbage collector.
 * All ggLang objects are allocated with this function.
 */
void* gg_alloc(size_t size);

/**
 * Explicitly frees memory (bypasses GC for manual control).
 */
void gg_free(void* ptr);

/**
 * Increments the reference count of an object (retained for API compat).
 */
void gg_retain(void* ptr);

/**
 * Decrements the reference count (retained for API compat).
 */
void gg_release(void* ptr);

/** Manual memory free — callable from ggLang as Memory.free(obj) */
void Memory_free(void* ptr);

/** Manual memory alloc — callable from ggLang as Memory.alloc(size) */
void* Memory_alloc(size_t size);

#endif /* !GG_NO_GC */

/* ============================================================
 * STRING (gg_string)
 * ============================================================ */

/**
 * Immutable string type with reference counting.
 */
typedef struct gg_string {
    int32_t ref_count;
    int32_t length;
    char* data;
} gg_string;

/** Creates a new string from a C string. */
gg_string* gg_string_from_cstr(const char* cstr);

/** Creates a new string from a buffer with length. */
gg_string* gg_string_from_buf(const char* buf, int32_t length);

/** Returns a pointer to the internal C string (read-only). */
const char* gg_string_cstr(gg_string* s);

/** Concatenates two strings and returns a new one. */
gg_string* gg_string_concat(gg_string* a, gg_string* b);

/** Compares two strings for equality. */
int gg_string_equals(gg_string* a, gg_string* b);

/** Returns the length of the string. */
int32_t gg_string_length(gg_string* s);

/** Returns a substring. */
gg_string* gg_string_substring(gg_string* s, int32_t start, int32_t length);

/** Checks if the string contains a substring. */
int gg_string_contains(gg_string* s, gg_string* sub);

/** Converts to uppercase. */
gg_string* gg_string_toUpper(gg_string* s);

/** Converts to lowercase. */
gg_string* gg_string_toLower(gg_string* s);

/** Removes leading and trailing whitespace. */
gg_string* gg_string_trim(gg_string* s);

/** Finds the index of a substring. */
int32_t gg_string_indexOf(gg_string* s, gg_string* sub);

/** Replaces occurrences. */
gg_string* gg_string_replace(gg_string* s, gg_string* old_str, gg_string* new_str);

/** Returns the string itself (for compatibility). */
gg_string* gg_string_toString(gg_string* s);

/* ============================================================
 * STRING CONVERSIONS
 * ============================================================ */

gg_string* gg_int_to_string(int32_t value);
gg_string* gg_long_to_string(int64_t value);
gg_string* gg_float_to_string(float value);
gg_string* gg_double_to_string(double value);
gg_string* gg_bool_to_string(int value);
gg_string* gg_char_to_string(char value);

/* ============================================================
 * DYNAMIC ARRAY (gg_array)
 * ============================================================ */

/**
 * Generic dynamic array.
 */
typedef struct gg_array {
    int32_t ref_count;
    int32_t length;
    int32_t capacity;
    int32_t elem_size;
    void* data;
} gg_array;

/** Creates a new array with initial size. */
gg_array* gg_array_new(int32_t elem_size, int32_t initial_size);

/** Returns the length of the array. */
int32_t gg_array_length(gg_array* arr);

/** Gets an element (returns generic pointer, use cast). */
void* gg_array_get_ptr(gg_array* arr, int32_t index);

/** Sets an element. */
void gg_array_set(gg_array* arr, int32_t index, void* value);

/** Macro for typed array access. */
#define gg_array_get(arr, index) (*((int*)(gg_array_get_ptr((arr), (index)))))
#define gg_array_get_typed(arr, type, index) (*((type*)(gg_array_get_ptr((arr), (index)))))

/* ============================================================
 * CONSOLE I/O
 * ============================================================ */

/** Writes a string followed by a newline. */
void gg_console_writeLine(gg_string* s);

/** Writes a string without a newline. */
void gg_console_write(gg_string* s);

/** Reads a line from stdin and returns it as gg_string. */
gg_string* gg_console_readLine(void);

/** Reads an integer from stdin. */
int32_t gg_console_readInt(void);

/* ============================================================
 * MATH FUNCTIONS
 * ============================================================ */

double gg_math_abs(double x);
double gg_math_sqrt(double x);
double gg_math_pow(double base_val, double exp);
double gg_math_min(double a, double b);
double gg_math_max(double a, double b);
int32_t gg_math_floor(double x);
int32_t gg_math_ceil(double x);
double gg_math_sin(double x);
double gg_math_cos(double x);
double gg_math_tan(double x);
double gg_math_log(double x);

/** PI constant */
#define GG_MATH_PI 3.14159265358979323846

/* ============================================================
 * FILE I/O (Files, Directory, Path)
 * ============================================================ */

/** Reads the entire contents of a file. Returns NULL on error. */
gg_string* gg_files_readAll(gg_string* path);

/** Writes a string to a file (overwrite). Returns 1 on success. */
int gg_files_writeAll(gg_string* path, gg_string* content);

/** Appends a string to a file. Returns 1 on success. */
int gg_files_append(gg_string* path, gg_string* content);

/** Checks if a file exists. */
int gg_files_exists(gg_string* path);

/** Deletes a file. Returns 1 on success. */
int gg_files_delete(gg_string* path);

/** Copies a file. Returns 1 on success. */
int gg_files_copy(gg_string* source, gg_string* dest);

/** Moves/renames a file. Returns 1 on success. */
int gg_files_move(gg_string* source, gg_string* dest);

/** Returns file size in bytes, or -1 on error. */
int32_t gg_files_size(gg_string* path);

/** Checks if a directory exists. */
int gg_directory_exists(gg_string* path);

/** Creates a directory. Returns 1 on success. */
int gg_directory_create(gg_string* path);

/** Removes an empty directory. Returns 1 on success. */
int gg_directory_remove(gg_string* path);

/** Returns the current working directory. */
gg_string* gg_directory_getCurrent(void);

/** Changes the current working directory. Returns 1 on success. */
int gg_directory_setCurrent(gg_string* path);

/** Combines two path segments. */
gg_string* gg_path_combine(gg_string* a, gg_string* b);

/** Returns the file name from a path. */
gg_string* gg_path_getFileName(gg_string* path);

/** Returns the extension from a path. */
gg_string* gg_path_getExtension(gg_string* path);

/** Returns the directory part of a path. */
gg_string* gg_path_getDirectory(gg_string* path);

/* ============================================================
 * CRYPTOGRAPHY
 * ============================================================ */

/** SHA-256 hash, returns hex string. */
gg_string* gg_crypto_sha256(gg_string* input);

/** MD5 hash, returns hex string. */
gg_string* gg_crypto_md5(gg_string* input);

/** SHA-1 hash, returns hex string. */
gg_string* gg_crypto_sha1(gg_string* input);

/** CRC32 checksum, returns hex string. */
gg_string* gg_crypto_crc32(gg_string* input);

/** HMAC-SHA256, returns hex string. */
gg_string* gg_crypto_hmacSha256(gg_string* input, gg_string* key);

/** Base64 encode. */
gg_string* gg_base64_encode(gg_string* input);

/** Base64 decode. */
gg_string* gg_base64_decode(gg_string* encoded);

/** Hex encode. */
gg_string* gg_hex_encode(gg_string* input);

/** Hex decode. */
gg_string* gg_hex_decode(gg_string* hexStr);

/** XOR encrypt (returns hex). */
gg_string* gg_xor_encrypt(gg_string* plaintext, gg_string* key);

/** XOR decrypt (from hex). */
gg_string* gg_xor_decrypt(gg_string* cipherHex, gg_string* key);

/** Random int in range [min, max). */
int32_t gg_random_nextInt(int32_t min, int32_t max);

/** Random alphanumeric string. */
gg_string* gg_random_nextString(int32_t length);

/** Random UUID v4. */
gg_string* gg_random_uuid(void);

/* ============================================================
 * NETWORKING
 * ============================================================ */

/** Resolves hostname to IP address string. */
gg_string* gg_network_resolve(gg_string* hostname);

/** Checks if host:port is reachable (TCP). */
int gg_network_ping(gg_string* host, int32_t port, int32_t timeoutMs);

/** Returns local hostname. */
gg_string* gg_network_getHostName(void);

/** Initializes network subsystem (Winsock on Windows, no-op elsewhere). */
void gg_network_init(void);

/** Shuts down network subsystem. */
void gg_network_shutdown(void);

/* ============================================================
 * OS UTILITIES
 * ============================================================ */

/** Returns platform name: "linux", "windows", "macos". */
gg_string* gg_os_platform(void);

/** Returns CPU architecture string. */
gg_string* gg_os_arch(void);

/** Gets an environment variable. */
gg_string* gg_os_getEnv(gg_string* name);

/** Sets an environment variable. Returns 1 on success. */
int gg_os_setEnv(gg_string* name, gg_string* value);

/** Removes an environment variable. Returns 1 on success. */
int gg_os_removeEnv(gg_string* name);

/** Exits the program. */
void gg_os_exit(int32_t code);

/** Returns current Unix timestamp. */
int32_t gg_os_time(void);

/** Sleeps for given milliseconds. */
void gg_os_sleep(int32_t ms);

/** Returns the number of CPU cores. */
int32_t gg_os_cpuCount(void);

/** Returns current username. */
gg_string* gg_os_userName(void);

/** Returns home directory. */
gg_string* gg_os_homeDir(void);

/** Returns temp directory. */
gg_string* gg_os_tempDir(void);

/** Returns path separator for current platform. */
gg_string* gg_os_pathSeparator(void);

/** Returns line ending for current platform. */
gg_string* gg_os_lineEnding(void);

/** Executes a shell command, returns stdout. */
gg_string* gg_process_exec(gg_string* command);

/** Executes a shell command, returns exit code. */
int32_t gg_process_run(gg_string* command);

/** Returns current process ID. */
int32_t gg_process_pid(void);

/** Returns high-resolution timestamp in milliseconds. */
int32_t gg_clock_now(void);

/** Returns current date as "YYYY-MM-DD". */
gg_string* gg_clock_date(void);

/** Returns current time as "HH:MM:SS". */
gg_string* gg_clock_time(void);

/** Returns current date+time as "YYYY-MM-DD HH:MM:SS". */
gg_string* gg_clock_dateTime(void);

/* ============================================================
 * EXTENSION METHODS — Type conversions & utilities
 * Called on primitive values: value.toString(), value.round(2), etc.
 * ============================================================ */

/* --- toString extensions --- */
const char* gg_ext_int_toString(int value);
const char* gg_ext_long_toString(long long value);
const char* gg_ext_double_toString(double value);
const char* gg_ext_float_toString(float value);
const char* gg_ext_bool_toString(int value);
const char* gg_ext_char_toString(char value);

/* --- toInt extensions --- */
int gg_ext_double_toInt(double value);
int gg_ext_float_toInt(float value);
int gg_ext_string_toInt(const char* value);
int gg_ext_long_toInt(long long value);
int gg_ext_bool_toInt(int value);
int gg_ext_char_toInt(char value);

/* --- toLong extensions --- */
long long gg_ext_int_toLong(int value);
long long gg_ext_double_toLong(double value);
long long gg_ext_string_toLong(const char* value);

/* --- toDouble / toDecimal extensions --- */
double gg_ext_int_toDouble(int value);
double gg_ext_long_toDouble(long long value);
double gg_ext_float_toDouble(float value);
double gg_ext_string_toDouble(const char* value);

/* --- toFloat extensions --- */
float gg_ext_int_toFloat(int value);
float gg_ext_double_toFloat(double value);
float gg_ext_string_toFloat(const char* value);

/* --- toBool extensions --- */
int gg_ext_int_toBool(int value);
int gg_ext_string_toBool(const char* value);
int gg_ext_double_toBool(double value);

/* --- toChar extensions --- */
char gg_ext_int_toChar(int value);
char gg_ext_string_toChar(const char* value);

/* --- round / ceil / floor on numeric types --- */
double gg_ext_double_round(double value, int decimals);
double gg_ext_float_round(float value, int decimals);
int gg_ext_double_roundToInt(double value);
int gg_ext_float_roundToInt(float value);
double gg_ext_double_ceil(double value);
double gg_ext_double_floor(double value);

/* --- abs on numeric types --- */
int gg_ext_int_abs(int value);
long long gg_ext_long_abs(long long value);
double gg_ext_double_abs(double value);
float gg_ext_float_abs(float value);

/* --- clamp / min / max --- */
int gg_ext_int_clamp(int value, int min_val, int max_val);
double gg_ext_double_clamp(double value, double min_val, double max_val);

/* --- string query extensions --- */
int gg_ext_string_length(const char* value);
int gg_ext_string_isEmpty(const char* value);
const char* gg_ext_string_toUpper(const char* value);
const char* gg_ext_string_toLower(const char* value);
const char* gg_ext_string_trim(const char* value);
const char* gg_ext_string_substring(const char* value, int start, int length);
int gg_ext_string_contains(const char* value, const char* sub);
int gg_ext_string_startsWith(const char* value, const char* prefix);
int gg_ext_string_endsWith(const char* value, const char* suffix);
int gg_ext_string_indexOf(const char* value, const char* sub);
const char* gg_ext_string_replace(const char* value, const char* old_str, const char* new_str);
const char* gg_ext_string_charAt(const char* value, int index);
const char* gg_ext_string_reverse(const char* value);
const char* gg_ext_string_padLeft(const char* value, int totalWidth, char padChar);
const char* gg_ext_string_padRight(const char* value, int totalWidth, char padChar);

/* ============================================================
 * HASH MAP — High-performance open-addressing hash table
 * ============================================================ */

/** Hash map entry (key-value pair). */
typedef struct gg_hashmap_entry {
    const char* key;       /* String key (owned by the map) */
    void* value;           /* Generic value pointer */
    uint8_t occupied;      /* 1 if slot is occupied */
    uint8_t deleted;       /* 1 if slot was deleted (tombstone) */
} gg_hashmap_entry;

/** Hash map with open addressing and Robin Hood hashing. */
typedef struct gg_hashmap {
    gg_hashmap_entry* buckets;
    int32_t capacity;
    int32_t count;
    int32_t elem_size;     /* Size of value stored */
    float load_factor;     /* Max load before resize (default 0.75) */
} gg_hashmap;

/** Creates a new hash map. elem_size = sizeof(value_type). */
gg_hashmap* gg_hashmap_new(int32_t elem_size);

/** Creates a hash map with specified initial capacity. */
gg_hashmap* gg_hashmap_new_capacity(int32_t elem_size, int32_t initial_capacity);

/** Inserts or updates a key-value pair. Value is copied (elem_size bytes). */
void gg_hashmap_put(gg_hashmap* map, const char* key, void* value);

/** Gets a value by key. Returns pointer to value or NULL if not found. */
void* gg_hashmap_get(gg_hashmap* map, const char* key);

/** Checks if a key exists. Returns 1 if found. */
int gg_hashmap_containsKey(gg_hashmap* map, const char* key);

/** Removes a key. Returns 1 if key was found and removed. */
int gg_hashmap_remove(gg_hashmap* map, const char* key);

/** Returns the number of entries. */
int32_t gg_hashmap_count(gg_hashmap* map);

/** Clears all entries. */
void gg_hashmap_clear(gg_hashmap* map);

/** Frees the hash map and all internal data. */
void gg_hashmap_free(gg_hashmap* map);

/* ============================================================
 * HASH SET — High-performance set using open addressing
 * ============================================================ */

typedef struct gg_hashset_entry {
    const char* key;
    uint8_t occupied;
    uint8_t deleted;
} gg_hashset_entry;

typedef struct gg_hashset {
    gg_hashset_entry* buckets;
    int32_t capacity;
    int32_t count;
    float load_factor;
} gg_hashset;

gg_hashset* gg_hashset_new(void);
gg_hashset* gg_hashset_new_capacity(int32_t initial_capacity);
int gg_hashset_add(gg_hashset* set, const char* key);
int gg_hashset_contains(gg_hashset* set, const char* key);
int gg_hashset_remove(gg_hashset* set, const char* key);
int32_t gg_hashset_count(gg_hashset* set);
void gg_hashset_clear(gg_hashset* set);
void gg_hashset_free(gg_hashset* set);

/* ============================================================
 * LINKED LIST — Doubly-linked list
 * ============================================================ */

typedef struct gg_list_node {
    void* data;
    struct gg_list_node* next;
    struct gg_list_node* prev;
} gg_list_node;

typedef struct gg_list {
    gg_list_node* head;
    gg_list_node* tail;
    int32_t count;
    int32_t elem_size;
} gg_list;

gg_list* gg_list_new(int32_t elem_size);
void gg_list_addFirst(gg_list* list, void* data);
void gg_list_addLast(gg_list* list, void* data);
void* gg_list_getFirst(gg_list* list);
void* gg_list_getLast(gg_list* list);
void* gg_list_get(gg_list* list, int32_t index);
int gg_list_removeFirst(gg_list* list);
int gg_list_removeLast(gg_list* list);
int32_t gg_list_count(gg_list* list);
void gg_list_clear(gg_list* list);
void gg_list_free(gg_list* list);

/* ============================================================
 * STACK — LIFO using dynamic array
 * ============================================================ */

typedef struct gg_stack {
    void* data;
    int32_t count;
    int32_t capacity;
    int32_t elem_size;
} gg_stack;

gg_stack* gg_stack_new(int32_t elem_size);
void gg_stack_push(gg_stack* stack, void* value);
void* gg_stack_peek(gg_stack* stack);
int gg_stack_pop(gg_stack* stack, void* out_value);
int32_t gg_stack_count(gg_stack* stack);
int gg_stack_isEmpty(gg_stack* stack);
void gg_stack_clear(gg_stack* stack);
void gg_stack_free(gg_stack* stack);

/* ============================================================
 * QUEUE — FIFO using circular buffer
 * ============================================================ */

typedef struct gg_queue {
    void* data;
    int32_t head;
    int32_t tail;
    int32_t count;
    int32_t capacity;
    int32_t elem_size;
} gg_queue;

gg_queue* gg_queue_new(int32_t elem_size);
void gg_queue_enqueue(gg_queue* queue, void* value);
int gg_queue_dequeue(gg_queue* queue, void* out_value);
void* gg_queue_peek(gg_queue* queue);
int32_t gg_queue_count(gg_queue* queue);
int gg_queue_isEmpty(gg_queue* queue);
void gg_queue_clear(gg_queue* queue);
void gg_queue_free(gg_queue* queue);

/* ============================================================
 * PROGRAM - ENTRY POINT
 * ============================================================ */

/**
 * Main function generated by the ggLang compiler.
 * The runtime provides the real main() that initializes the runtime
 * and calls the user's Program_main().
 */
void Program_main(void);

#endif /* GG_RUNTIME_H */
