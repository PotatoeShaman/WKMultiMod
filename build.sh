#!/bin/bash

cd ./src/Shared
dotnet build -c Release

cd ../Core
dotnet build -c Release

cd ../../bin/Release

rm "./WKMultiMod_local.tar.gz"
tar -czvf ../WKMultiMod_local.tar.gz *
mv ../WKMultiMod_local.tar.gz ./