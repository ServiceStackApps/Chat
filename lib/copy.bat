SET BUILD=Release
REM SET BUILD=Debug

COPY C:\src\ServiceStack\src\ServiceStack.Razor\bin\%BUILD%\net45\* .
COPY C:\src\ServiceStack\src\ServiceStack.Server\bin\%BUILD%\net45\* .
COPY C:\src\ServiceStack\src\ServiceStack.Authentication.OAuth2\bin\%BUILD%\net45\ServiceStack.Authentication.OAuth2.dll .
