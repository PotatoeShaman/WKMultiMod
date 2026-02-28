#!/bin/bash

cd ./src/Shared
dotnet build -c Release

cd ../Core
dotnet build -c Release

cd ../../