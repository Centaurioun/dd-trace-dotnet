# ******************************************************
# Project definition
# ******************************************************

project("Datadog.Linux.ApiWrapper" VERSION 1.0.0)

# ******************************************************
# Compiler options
# ******************************************************

# Sets compiler options
add_compile_options(-std=c11 -fPIC) 


# ******************************************************
# Environment detection
# ******************************************************

SET(API_WRAPPER_BASENAME Datadog.Linux.ApiWrapper)
SET(API_WRAPPER_SHARED_LIB_NAME ${API_WRAPPER_BASENAME}.x64)

SET(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${DEPLOY_DIR})
SET(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${DEPLOY_DIR})
SET(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${DEPLOY_DIR})

# ******************************************************
# Define shared target
# ******************************************************

add_library(${API_WRAPPER_SHARED_LIB_NAME} SHARED
    functions_to_wrap.c
)

# Define linker libraries
target_link_libraries(${API_WRAPPER_SHARED_LIB_NAME}
    -pthread
    -ldl
)

set_target_properties(${API_WRAPPER_SHARED_LIB_NAME} PROPERTIES PREFIX "")

