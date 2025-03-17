# PROBUILD BACKEND

![C#](https://img.shields.io/badge/c%23-%23239120.svg?style=for-the-badge&logo=c-sharp&logoColor=white) ![GitHub Actions](https://img.shields.io/badge/github%20actions-%232671E5.svg?style=for-the-badge&logo=githubactions&logoColor=white) ![SonarLint](https://img.shields.io/badge/SonarLint-CB2029?style=for-the-badge&logo=sonarlint&logoColor=white) ![Notepad++](https://img.shields.io/badge/Notepad++-90E59A.svg?style=for-the-badge&logo=notepad%2B%2B&logoColor=black) ![VScode](https://img.shields.io/badge/Visual_Studio_Code-0078D4?style=for-the-badge&logo=visual%20studio%20code&logoColor=white) ![.Net](https://img.shields.io/badge/.NET-5C2D91?style=for-the-badge&logo=.net&logoColor=white)

ProBuild is a comprehensive construction project management API built using ASP.NET Core. It facilitates the management of construction projects by allowing users to register, log in, create projects, submit bids, and receive notifications. The system supports different user roles including Foreman, Contractor, Subcontractor,Product Owner and Client.

## TableofContents

1. [Features](##Features)

2. [TechnologiesUsed](##TechnologiesUsed)

3. [GettingStarted](##GettingStarted)

4. [Prerequisites](###Prerequisites)

5. [Setup](###Setup)

6. [RunningtheProject](###RunningtheProject)

7. [APIEndpoints](##APIEndpoints)

8. [Models](##Models)

9. [Contributing](##Contributing)

10. [License](##License)

## Features

* User Registration and Authentication (JWT-based)
* Role-based Access Control
* Project Management (CRUD Operations)
* Bid Management
* Real-time Notifications using WebSockets
* Document and Permit Upload
* Verification via Email or SMS

## Technologies Used

* ASP.NET Core 8.0
* Entity Framework Core
* SQL Server
* JWT Authentication
* WebSockets

Swagger for API documentation

## GettingStarted

### Prerequisites

* [.NET 8.0 SDK/Runtime](https://dotnet.microsoft.com/en-us/download)
* [SQL server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads)
* [Visual Studio 2022](https://visualstudio.microsoft.com/downloads/)/[VSCode](https://code.visualstudio.com/download)
* [Postman](https://www.postman.com/downloads/) for testing the API
* [SSMS](https://aka.ms/ssmsfullsetup)/[Dbeaver](https://dbeaver.io/download/)

### Setup

**1. Install/Restore Dependencies:**

After cloning The Repository change directory to ***ProbuildBackend***.

```bash
cd ProbuildBackend
```

Install dotnet-ef globlally and restore all dependencies required by application.

```bash
dotnet tool install --global dotnet-ef
dontet restore
```

**2. Set up the database:**

|Ensuure you have [SQL server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) installed for OS distribution. (Windows/Ubuntu/MacOS) and also install for [SSMS](https://aka.ms/ssmsfullsetup) Windows users, for other Users you can use [Dbeaver](https://dbeaver.io/download/)


***Recommended***

You can connect your DB using Microsoft SQL Studio how ever, I would recommend to use DBeaver it lighter and offers more feactures which are available across multiple DB's. To Connect SQL Server 2022 to DBeaver do the following:

- Open ***Sql Server Configuration Manager*** navigate to ***SQL Server Network Configuration*** select ***Protocols for MSSQLSERVER*** and **enable** ***TCP/IP*** feature.
- Restart ***SQL Server(MSSQLSERVER)*** under the ***SQL Server Services***.
- Open DBeaver and Connect to a new SQL DB and ensure under the settings section you check ***Show All Schemas*** , ***Trust Server Certificate*** . On the Authentication Section choose the authentication type to be ***Windows Authentication***
- Test Connection, if connection is successfull save , if you meet any challenges take note of the error and communicate with [Engineering team](mailto:prince@initd-it.co.za)

Then export connection string to envrionment variables depending on your OS Distribution

***Windows***

```powershell
[Environment]::SetEnvironmentVariable("DB_CONNECTION_STRING", "Data Source=YOUR_MACHINE_NAME;Initial Catalog=ProBuildDb;Integrated Security=True;Trust Server Certificate=True;", "User")
```

***Unix***

```bash
export DB_CONNECTION_STRING="Data Source=YOUR_MACHINE_NAME;Initial Catalog=ProBuildDb;Integrated Security=True;Trust Server Certificate=True;"
```

Run the following commands to apply migrations and create the database:

```bash
dotnet ef database update
```

### RunningtheProject

**1. Run the API:**

When you can run the project from Visual Studio by pressing F5 or using the terminal if on VSCCode:

```bash
dotnet run
```

### Deploy Application usin Docker

When using Docker you will need to download images from Initd-It Service Private Registry in Github

```bash
docker pull ghcr.io/initd-itservices/probuild-ui:latest
```

**NB**: You can only pull images if you actually rights to pull and push into **initd-It Service** Organization in Github meaning you should hava [developer token](https://github.com/settings/tokens) generated

after whhich set the **DB_CONNECTION_STRING** url as a system variables to your host machine 

***Windows***
```powershell
[Environment]::SetEnvironmentVariable("DB_CONNECTION_STRING", "Data Source=YOUR_MACHINE_NAME;Initial Catalog=ProBuildDb;Integrated Security=True;Trust Server Certificate=True;", "User")
```

***Unix***

```bash
export DB_CONNECTION_STRING="Data Source=YOUR_MACHINE_NAME;Initial Catalog=ProBuildDb;Integrated Security=True;Trust Server Certificate=True;"
```

Then lastly run the docker compose file on the root of the project.

```bash
docker-compose up -d
```

or

```bash
docker compose up -d
```

Once deployed the application will be available on port **5000**

**2. Access the API documentation:**

Open your browser and navigate to https://localhost:5001/swagger to view and interact with the API endpoints using Swagger.

## APIEndpoints

### Account

POST /api/Account/register - Register a new user.

POST /api/Account/login - Authenticate a user and return a JWT token.

### Jobs

GET /api/Jobs - Retrieve all job.

GET /api/Jobs/{id} - Retrieve a specific job by ID.

POST /api/Jobs - Create a new job.

PUT /api/Jobs/{id} - Update an existing job.

DELETE /api/Jobs/{id} - Delete a job.

### Projects

GET /api/Projects - Retrieve all projects.

GET /api/Projects/{id} - Retrieve a specific project by ID.

POST /api/Projects - Create a new project.

PUT /api/Projects/{id} - Update an existing project.

DELETE /api/Projects/{id} - Delete a project.

### Bids

GET /api/Bids - Retrieve all bids.

GET /api/Bids/{id} - Retrieve a specific bid by ID.

POST /api/Bids - Submit a new bid.

PUT /api/Bids/{id} - Update an existing bid.

DELETE /api/Bids/{id} - Delete a bid.

### Notifications

GET /api/Notifications - Retrieve all notifications.

GET /api/Notifications/{id} - Retrieve a specific notification by ID.

POST /api/Notifications - Send a new notification.

## Models

### User

Id: string

FirstName: string

Surname: string

Email: string

Cell: string

CompanyRegNo: string

VatNo: string

Role: string (Contractor, Foreman, Client, Subcontractor,Project Owner, Project Manager)

OperatingServices: string

OperatingArea: string

Password: string

### Project

Id: int

ProjectName: string

JobType: string

Qty: int

DesiredStartDate: DateTime

WallStructure: string

WallStructureStatus: string

WallInsulation: string

WallInsulationSubtask: string

WallInsulationStatus: string

RoofStructure: string

RoofStructureSubtask: string

RoofStructureStatus: string

RoofType: string

RoofTypeSubtask: string

RoofTypeStatus: string

RoofInsulation: string

RoofInsulationSubtask: string

RoofInsulationStatus: string

Foundation: string

FoundationSubtask: string

FoundationStatus: string

Finishes: string

FinishesSubtask: string

FinishesStatus: string

ElectricalSupplyNeeds: string

ElectricalSupplyNeedsSubtask: string

ElectricalStatus: string

Stories: int

BuildingSize: double

BlueprintPath: string

OperatingArea: string

ForemanId: string

ContractorId: string

SubContractorFinishesId: string

SubContractorRoofTypeId: string

SubContractorFoundationId: string

SubContractorRoofStructureId: string

SubContractorWallStructureId: string

SubContractorRoofInsulationId: string

SubContractorWallInsulationId: string

SubContractorElectricalSupplyNeedsId: string

Bids: ICollection<Bid>

Notifications: ICollection<Notification>

### Bid

Id: int

Task: string

Quote: decimal

Duration: string

ProjectId: int (Foreign Key)

UserId: string (Foreign Key)

### Notification

Id: int

Message: string

Timestamp: DateTime

ProjectId: int (Foreign Key)

UserId: int (Foreign Key)

Recipients: List<string>

## Contributing

We welcome contributions! Please fork the repository and submit pull requests for any improvements or new features.

## License

This project is licensed under the MIT License. See the [LICENSE](https://) file for details.
