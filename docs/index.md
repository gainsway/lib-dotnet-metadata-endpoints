## lib-dotnet-metadata-endpoints

This library add metadata endpoints to a .NET Core Api

## Configuration

1. Install `Gainsway.MetadataEndpoints` package in the target project
  ```sh
  dotnet add package Gainsway.MetadataEndpoints
  ```
2. Register the Metadata Endpoints services in `Program.cs`:
  ```csharp
    /// Program.cs

    //... 
    app.AddMetadataEndpoint();

    //...
    app.MapControllers();
    app.Run();
  ```

## Environment variables

The metadata endpoint will return data in the following environment variables:
- `GIT_SHORT_SHA`