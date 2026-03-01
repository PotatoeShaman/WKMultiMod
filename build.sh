#!/bin/bash

cd ./src/Shared
dotnet build -c Release

cd ../Core
dotnet build -c Release

cd ../../bin/Release
tar -cvf "./WKMultiMod_local.tar" "./"