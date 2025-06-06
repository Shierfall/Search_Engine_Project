# Search Engine: Build & Run Instructions

This project combines a .NET 9 backend (serving both API and static files) with a React/TypeScript frontend (Vite + Tailwind). Below are the minimal steps to get it up and running locally.

---
## Install with Docker
1. Build the image
    ```bash
    docker build -t search-engine .

2. Run without a data file (loads already persisted index (or none if none persisted)):
    ```bash
    docker run --rm -p 5268:5268 search-engine

App will listen on http://localhost:5268/
3. Run with a data file:
```bash
    docker run --rm -v $(pwd)/50MB.txt:/data/50MB.txt -p 5268:5268 my-search-engine /data/filename.txt
NOTE: The --rm flag in the docker command means that Docker will automatically remove the container and its filesystem
    as soon as it exits. So if you want to keep the local Docker container and its contents, then omit the --rm flag

### Prerequisites
1. **.NET 9 SDK**  
   Install from https://dotnet.microsoft.com/download/dotnet/9.0  
   Verify with:
   ```bash
   dotnet --version   # should print something like “9.x.x”

2. **Node.js (v16 or higher)**
    Install from https://nodejs.org/
    Verify with:
    ```bash 
    node --version    # v16.x, v18.x or later
    npm --version     # v8.x or later

### Build and Run

1. Navigate to the project root (where SearchEngine.csproj is) and run
    ```bash
    dotnet restore
    dotnet build
2. This technically should be all you need. Run the program with one of the text files from the Project Info website:
    ```bash
    dotnet run WestburyLab.wikicorp.201004_.txt
3. Once processing is complete (or rebuild is complete), open a browser and go to http://localhost:5268/ to use the search engine
4. If you've already run the program after giving it a file, you can simply run
    ```bash
    dotnet run
This will simply rebuild the index with the persisted data retrieved from the file you gave earlier.

### Troubleshooting

1. If you run into issues with the frontend you can run our prepublish script; navigate to the project root:
    ```bash
    chmod +x prepublish.sh
    ./prepublish.sh

2. If that still doesn't work then you have to build it manually:
    ``` bash
    cd frontend
    npm install
    npm run build
    cd ..
    chmod +x prepublish.sh
    ./prepublish.sh

## Change ports
If you need to change ports, set the ASPNETCORE_URLS env variable before running:
```bash
    export ASPNETCORE_URLS="http://localhost:8080;https://localhost:8443"
    dotnet run

