cmake_minimum_required(VERSION 3.20)
project(ygoengine LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

set(YGOPRO_CORE_DIR "C:/Users/y/AppData/Local/YGO-PC-Full/OpenDuelEngine/ygopro-core" CACHE PATH "Path to ygopro-core source")

file(GLOB CORE_CPP "${YGOPRO_CORE_DIR}/*.cpp" "${YGOPRO_CORE_DIR}/RNG/*.cpp")

# Apply project changes to a copy of the processor, keeping the shared core source intact.
find_package(Git REQUIRED)
set(CORE_PROCESSOR "${YGOPRO_CORE_DIR}/processor.cpp")
set(CORE_PATCHES
    "${CMAKE_CURRENT_SOURCE_DIR}/master_duel_chain_limit.patch"
    "${CMAKE_CURRENT_SOURCE_DIR}/battle_comparison.patch"
)
set(CORE_PATCH_STAGE "${CMAKE_CURRENT_BINARY_DIR}/core-patch-stage")
set(CORE_PROCESSOR_PATCHED "${CMAKE_CURRENT_BINARY_DIR}/processor.cpp")
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${CORE_PROCESSOR}" ${CORE_PATCHES})
file(MAKE_DIRECTORY "${CORE_PATCH_STAGE}")
file(READ "${CORE_PROCESSOR}" CORE_PROCESSOR_SOURCE)
file(WRITE "${CORE_PATCH_STAGE}/processor.cpp" "${CORE_PROCESSOR_SOURCE}")
foreach(CORE_PATCH IN LISTS CORE_PATCHES)
    execute_process(
        COMMAND "${CMAKE_COMMAND}" -E env "GIT_CEILING_DIRECTORIES=${CMAKE_CURRENT_BINARY_DIR}"
            "${GIT_EXECUTABLE}" apply --no-index --ignore-space-change "${CORE_PATCH}"
        WORKING_DIRECTORY "${CORE_PATCH_STAGE}"
        RESULT_VARIABLE CORE_PATCH_RESULT
        ERROR_VARIABLE CORE_PATCH_ERROR
    )
    if(NOT CORE_PATCH_RESULT EQUAL 0)
        message(FATAL_ERROR "Could not apply ${CORE_PATCH}:\n${CORE_PATCH_ERROR}")
    endif()
endforeach()
configure_file("${CORE_PATCH_STAGE}/processor.cpp" "${CORE_PROCESSOR_PATCHED}" COPYONLY)
list(REMOVE_ITEM CORE_CPP "${CORE_PROCESSOR}")
list(APPEND CORE_CPP "${CORE_PROCESSOR_PATCHED}")

file(GLOB LUA_SRC "${YGOPRO_CORE_DIR}/lua/src/*.c")
list(FILTER LUA_SRC EXCLUDE REGEX ".*(lbitlib|lcorolib|ldblib|linit|loadlib|loslib|ltests|lua|luac|lutf8lib|onelua)\\.c$")
set_source_files_properties(${LUA_SRC} PROPERTIES LANGUAGE CXX)

if(IOS)
    set(YGOENGINE_LIBRARY_TYPE STATIC)
else()
    set(YGOENGINE_LIBRARY_TYPE SHARED)
endif()

add_library(ygoengine ${YGOENGINE_LIBRARY_TYPE}
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

if(IOS)
    set_target_properties(ygoengine PROPERTIES OUTPUT_NAME "ygo_native")
else()
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
endif()
