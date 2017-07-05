#!/bin/sh

FINAL_RELEASE_DIR="`pwd`"
PLUGIN_XML_DIR="`pwd`/Source/Plugins"
RELEASE_DIR="`pwd`/Source/bin/Debug"

xbuild /p:Configuration=Debug /p:TargetFrameworkVersion="v4.0" otitemeditor.sln

# rm -rf $FINAL_RELEASE_DIR
# mkdir -p $FINAL_RELEASE_DIR || exit
# mkdir -p $FINAL_RELEASE_DIR/plugins
cp $RELEASE_DIR/*.exe $FINAL_RELEASE_DIR
cp $RELEASE_DIR/ImageSimilarity.dll $FINAL_RELEASE_DIR
cp $RELEASE_DIR/PluginInterface.dll $FINAL_RELEASE_DIR
cp $RELEASE_DIR/plugin*.dll $FINAL_RELEASE_DIR/plugins
cp $PLUGIN_XML_DIR/*.xml $FINAL_RELEASE_DIR/plugins
