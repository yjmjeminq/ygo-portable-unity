cmake_minimum_required(VERSION 3.20)
project(ygoengine LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

set(YGOPRO_CORE_DIR "C:/Users/y/AppData/Local/YGO-PC-Full/OpenDuelEngine/ygopro-core" CACHE PATH "Path to ygopro-core source")

file(GLOB CORE_CPP "${YGOPRO_CORE_DIR}/*.cpp" "${YGOPRO_CORE_DIR}/RNG/*.cpp")
file(GLOB LUA_SRC "${YGOPRO_CORE_DIR}/lua/src/*.c")
list(FILTER LUA_SRC EXCLUDE REGEX ".*(lbitlib|lcorolib|ldblib|linit|loadlib|loslib|ltests|lua|luac|lutf8lib|onelua)\\.c$")
set_source_files_properties(${LUA_SRC} PROPERTIES LANGUAGE CXX)

add_library(ygoengine SHARED
    ygoengine.cpp
    ${CORE_CPP}
    ${LUA_SRC}
)

target_include_directories(ygoengine PRIVATE
    "${YGOPRO_CORE_DIR}"
    "${YGOPRO_CORE_DIR}/RNG"
    "${YGOPRO_CORE_DIR}/lua/src"
)

target_compile_definitions(ygoengine PRIVATE
    NOMINMAX
    YGOENGINE_EXPORTS
    _CRT_SECURE_NO_WARNINGS
)

if(MSVC)
    target_compile_options(ygoengine PRIVATE
        /O2
        /GR-
        /W2
        /wd4244 /wd4267 /wd4996
        "/FI${YGOPRO_CORE_DIR}/lua/luaconf-customize.h"
    )
    set_source_files_properties(${CORE_CPP} ygoengine.cpp PROPERTIES COMPILE_OPTIONS "/EHsc")
else()
    target_compile_options(ygoengine PRIVATE -O2 -fno-rtti -include "${YGOPRO_CORE_DIR}/lua/luaconf-customize.h")
endif()

set_target_properties(ygoengine PROPERTIES
    PREFIX ""
    OUTPUT_NAME "ygoengine"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/../Assets/Plugins/x86_64"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_CURRENT_SOURCE_DIR}/../Assets/Plugins/x86_64"
    RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO "${CMAKE_CURRENT_SOURCE_DIR}/../Assets/Plugins/x86_64"
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/../Assets/Plugins/x86_64"
)

add_executable(ygo_smoke smoke_test.cpp)
target_link_libraries(ygo_smoke PRIVATE ygoengine)
set_target_properties(ygo_smoke PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/bin"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_CURRENT_BINARY_DIR}/bin"
)
