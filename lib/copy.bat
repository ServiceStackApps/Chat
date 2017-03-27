SET BUILD=Release
REM SET BUILD=Debug

COPY C:\src\ServiceStack\src\ServiceStack.Razor\bin\%BUILD%\* .
COPY C:\src\ServiceStack\src\ServiceStack.Server\bin\%BUILD%\* .
COPY C:\src\ServiceStack\src\ServiceStack.Authentication.OAuth2\bin\%BUILD%\ServiceStack.Authentication.OAuth2.dll .
