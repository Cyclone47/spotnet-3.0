$baseRecon = 'd:\sourcecode\src\Spotnet'
New-Item -ItemType Directory -Path $baseRecon -Force | Out-Null
New-Item -ItemType Directory -Path "$baseRecon\Spotnet" -Force | Out-Null
New-Item -ItemType Directory -Path "$baseRecon\Spotnet.Enc" -Force | Out-Null
New-Item -ItemType Directory -Path "$baseRecon\Spotnet.Tests" -Force | Out-Null
New-Item -ItemType Directory -Path "$baseRecon\lib" -Force | Out-Null

# Copy third party libraries
Copy-Item -Path 'd:\sourcecode\spotnet-2.0.0.284-binary\*.dll' -Destination "$baseRecon\lib" -Force
Copy-Item -Path 'd:\sourcecode\spotnet-2.0.0.284-binary\*.exe' -Destination "$baseRecon\lib" -Force
Copy-Item -Path 'd:\sourcecode\spotnet-2.0.0.284-binary\Data' -Destination "$baseRecon\Spotnet\Data" -Recurse -Force
Copy-Item -Path 'd:\sourcecode\spotnet-2.0.0.284-binary\Resources' -Destination "$baseRecon\Spotnet\Resources" -Recurse -Force

# Copy decompiled source code into Spotnet project
Copy-Item -Path 'd:\sourcecode\decompiled_200\Spotnet\*' -Destination "$baseRecon\Spotnet" -Recurse -Force

# Copy decompiled XAML into Spotnet project
Copy-Item -Path 'd:\sourcecode\decompiled_200\Spotnet_xaml\*' -Destination "$baseRecon\Spotnet" -Recurse -Force

Write-Host "Reconstructed workspace assembled."
