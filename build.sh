#!/bin/bash

cd ./src/Shared
dotnet build -c Release

cd ../Core
dotnet build -c Release

cd ../../bin/

rm "./Release/WKMultiMod_local.zip"
#tar -cjvf ../WKMultiMod_local.zip -C ./ *

perl -e '
  use strict;
  use warnings;
  use autodie;
  use IO::Compress::Zip qw(:all);
  zip [
    <"Release/*">
  ] => "WKMultiMod_local.zip",
       FilterName => sub { s[^Release/][] },
       Zip64 => 0,
  or die "Zip failed: $ZipError\n";
'

mv ./WKMultiMod_local.zip ./Release/