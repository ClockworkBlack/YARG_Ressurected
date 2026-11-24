cmake_minimum_required(VERSION 3.18)
project(sqlite3 C)
# sqlite3.c / sqlite3.h (the amalgamation) are downloaded by build-android-native.sh
add_library(sqlite3 SHARED sqlite3.c)
target_compile_definitions(sqlite3 PRIVATE
    SQLITE_THREADSAFE=1
    SQLITE_ENABLE_FTS5
    SQLITE_ENABLE_JSON1
    SQLITE_DEFAULT_FOREIGN_KEYS=1
    SQLITE_ENABLE_COLUMN_METADATA)
target_link_libraries(sqlite3 PRIVATE dl m)
