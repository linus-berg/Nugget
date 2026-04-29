#!/bin/bash

# Configuration
REGISTRY_URL="http://localhost:5116/v3/index.json"
PACKAGE_ID="SamplePackage"
VERSION="1.0.0"
TEMP_DIR="temp_restore_test"

echo "Testing pull (restore) for $PACKAGE_ID version $VERSION from $REGISTRY_URL..."

# Create a temporary project to test restore
mkdir -p $TEMP_DIR
cd $TEMP_DIR
dotnet new console --force

# Add the local registry as a source and allow insecure connections
dotnet nuget add source "$REGISTRY_URL" --name "NuggetRegistry" --configfile nuget.config --allow-insecure-connections

# Add the package from our registry
dotnet add package "$PACKAGE_ID" --version "$VERSION" --source "$REGISTRY_URL"

if [ $? -eq 0 ]; then
    echo "Pull (restore) successful!"
else
    echo "Pull failed."
    cd ..
    rm -rf $TEMP_DIR
    exit 1
fi

# Cleanup
cd ..
rm -rf $TEMP_DIR
echo "Cleanup complete."
