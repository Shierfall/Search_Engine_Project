#!/bin/bash

# get the directory where the script is located (your backend project directory)
BACKEND_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# make sure wwwroot exists
mkdir -p "$BACKEND_DIR/wwwroot"

# clean existing files in wwwroot
rm -rf "$BACKEND_DIR/wwwroot/*"

# navigate to frontend directory (now it's in the same directory level)
cd "$BACKEND_DIR/frontend"

# install dependencies and build
npm install
npm run build

# copy frontend build to wwwroot
cp -r dist/* "$BACKEND_DIR/wwwroot/"
