Sample server for Macrobond Client Application Web Provider API
============================

Sample server written in C#/.NET that can serve the Macrobond Application with time series.

By implementing the Web Provider API in your service, the Macrobond Application can retrieve time series that you provide. The service can also offer one ore more of the following functions that enables additional functionality for this database in the Macrobond application:
- Edit: The user can use the Macrobond Application to upload and delete series
- Browse: Gives the user the option to navigate the database using a tree structure in the same way as with the databases provided by Macrobond.
- Search: Gives the user the option to search the database by entering a text that is then passed to the API. The API typically uses this to provide text searching.

You can find the documentation of the API [here](https://help.macrobond.com/technical-information/the-macrobond-web-api-provider/).
## Repository structure
To download the source code for the sample server, you can use a git command
```bash
git clone https://github.com/macrobond/client-webapi-sample.git client-webapi-sample
```
This repository contains two folders, one for password based authentication named *client-webapi-sample-password* and one for token based authentication named *client-webapi-sample-token*. The structure of the folders are quite similar, they both contain a solution named SeriesServer.
## Building the server
The sample is using .NET 8.0. You can find more information and download the SDK at the [Microsoft site](https://dotnet.microsoft.com/download/dotnet-core)

The solution file SeriesServer.sln can be opened in Visual Studio 2022, but you can also compile it from the command prompt and use other tools such as [Visual Studio Code](https://code.visualstudio.com/).
The sample should be able to compile and run on Windows and Linux.

if you do not want to use a development environment, you can build and run from the command prompt.
```bash
cd client-webapi-sample/SeriesServer
dotnet run
```
## Running the server
By default, the sample will use the Kestrel Web server implementation in .NET Core.
The server will listen to http://localhost:5000, but you can use the command line parameter --urls to configure another address.
For more details about the Kestrel server see the [Microsoft documentation](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel).

## Testing the server
When the server is up an running, you can configure the Macrobond Application to be aware of this database.
1. In the respcitive folder in this repository there is a sample configuration file called configWebApi.xml. Make sure that it points to the server URL. The token variant will have a configuration parameter *UseOAuth* set.
1. In the Macrobond application, select Configure|Settings then go to the "My series" tab.
1. Select to add a new database of type "Web API"
1. Call it `Test`, set the prefix to `test` and specify the path of the config file.
1. For password authentication
    1. Fill username.
    1. Fill password.

1. Select OK. The application will then reload all settings which takes a few seconds.

In the list of databases at the top of the data trees in the application (Vertical tabs|Analytics|Browse|Top field|Bottom of list), you should now find a database called "Test". You should be able to see the data tree that is provided by the sample server.
If you make changes to the config file, you need to restart the application for changes to take effect.
