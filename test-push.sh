#!/bin/bash

# Configuration
REGISTRY_URL="http://localhost:5116"
SAMPLE_PACKAGE="SamplePackage.1.0.0.nupkg"
POPULAR_PACKAGE_ID="Newtonsoft.Json"
POPULAR_PACKAGE_VERSION="13.0.3"
POPULAR_PACKAGE="$POPULAR_PACKAGE_ID.$POPULAR_PACKAGE_VERSION.nupkg"

# 1. Ensure SamplePackage exists
if [ ! -f "$SAMPLE_PACKAGE" ]; then
    echo "Error: $SAMPLE_PACKAGE not found. Run 'dotnet pack SamplePackage' first."
    exit 1
fi

# 2. Download Newtonsoft.Json from nuget.org
if [ ! -f "$POPULAR_PACKAGE" ]; then
    echo "Downloading $POPULAR_PACKAGE from nuget.org..."
    curl -L -o "$POPULAR_PACKAGE" "https://www.nuget.org/api/v2/package/$POPULAR_PACKAGE_ID/$POPULAR_PACKAGE_VERSION"
fi

# 3. Create temporary nuget.config to allow HTTP
cat <<EOF > nuget.config
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="Nugget" value="$REGISTRY_URL/v3/packages" allowInsecureConnections="true" />
  </packageSources>
  <packageSourceCredentials>
  </packageSourceCredentials>
  <packageSourceMapping>
  </packageSourceMapping>
  <config>
    <add key="allowInsecureConnections" value="true" />
  </config>
</configuration>
EOF

# 4. Push function
push_package() {
    local pkg=$1
    echo "Pushing $pkg to $REGISTRY_URL..."
    dotnet nuget push "$pkg" \
        --source "Nugget" \
        --api-key "nugget-key" \
        --skip-duplicate \
        --allow-insecure-connections \
        --configfile nuget.config

    if [ $? -eq 0 ]; then
        echo "Successfully pushed $pkg"
    else
        echo "Failed to push $pkg"
        # Clean up config on failure
        rm nuget.config
        return 1
    fi
}

# 5. Execute pushes
#push_package "$SAMPLE_PACKAGE" || exit 1
#push_package "$POPULAR_PACKAGE" || exit 1
push_package nifikit.0.0.3.nupkg
#push_package nifikit.0.0.2.nupkg

# 6. Cleanup
rm nuget.config
echo "All test pushes completed successfully!"
