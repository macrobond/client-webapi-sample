Sample server for Macrobond Client Application Web Provider API
============================

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)

Sample server written in C#/.NET Core that can serve the Macrobond Application with time series.

By implementing the Web Provider API in your service, the Macrobond Application can retrieve time series that you provide. The service can also offer one ore more of the following functions that enables additional functionality for this database in the Macrobond application:
- Edit: The user can use the Macrobond Application to upload and delete series
- Browse: Gives the user the option to navigate the database using a tree structure in the same way as with the databases provided by Macrobond.
- Search: Gives the user the option to search the database by entering a text that is then passed to the API. The API typically uses this to provide text searching.

You can find the documentation of the API [here](https://help.macrobond.com/?page_id=8589&preview=true).

## Building the server
The sample is using .NET Core 3.1. The solution file SeriesServer.sln can be opened in Visual Studio 2019, but you can also compile it from the command prompt and use other tools such as [Visual Studio Code](https://code.visualstudio.com/).
The sample should be able to compile and run on Windows and Linux.

## Running the server

## Testing the server
