# Fleetify - Delivery & Transport Management System

**Final Year Project (FYP)**  
*Faculty of Computing & Information Technology (FCIT), University of the Punjab, Lahore*  
*Govt. Graduate College of Commerce, Gujranwala*  
**Project ID:** GGCC-GRW-FYP22-07 | **Group-07**  
**Authors:** Zain Ali (075897) &amp; Ammar Soban (075915)  
**Supervisor:** Prof. Waqas Jamil  

---

## 🛠️ Technology Stack
- **Backend:** ASP.NET Core MVC (.NET 8.0)
- **Database:** Microsoft SQL Server (`MSSQL$SQLEXPRESS`)
- **ORM:** Entity Framework Core (`Microsoft.EntityFrameworkCore.SqlServer`)
- **Authentication & Security:** JWT (JSON Web Token) + ASP.NET Core Cookie Authentication & Role-based Authorization
- **Frontend:** Bootstrap 5, Bootstrap Icons, Chart.js, Vanilla CSS3 & Modern JavaScript
- **AI Modules:**
  1. **AI Cost Estimation Module:** Algorithmic calculation based on distance, parcel weight, vehicle type, and service route complexity.
  2. **AI Predictive Maintenance Module:** Automatic vehicle usage, mileage interval, and condition status analysis to forecast servicing needs before breakdowns occur.
  3. **Notification System:** In-app operational alerts and progress updates.

---

## 🚀 Getting Started

### 1. Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Microsoft SQL Server / SQL Server Express (running locally as `.\SQLEXPRESS`)

### 2. Running the Application
From the project directory:
```bash
dotnet run
```
The application will automatically:
1. Connect to `Server=.\\SQLEXPRESS;Database=FleetifyDb;...`
2. Ensure the database schema is created cleanly.
3. Launch the local web server at `http://localhost:5000` or `https://localhost:5001`.

---

## 👥 Authentic Roles & Portals

| Role | Access Portal | Description |
| :--- | :--- | :--- |
| **Admin** | `/Account/Login?role=Admin` | Full operations dashboard, driver & vehicle fleet management, order assignments, predictive maintenance alerts. |
| **Driver** | `/Account/Login?role=Driver` | Driver portal: view assigned deliveries, accept/reject, update live status (In-Transit / Delivered). |
| **Customer** | `/Account/Login?role=Customer` | Customer portal: book deliveries with instant AI cost calculation, live tracking timeline. |

---

## 📊 Modules & Functional Requirements Implemented
- **FR-1 & FR-2:** User Registration & JWT/Session Login with role switching (`Customer`, `Admin`, `Driver`).
- **FR-3:** Delivery Request Management with pickup/dropoff locations and parcel parameters.
- **FR-4:** Driver Management (Admin add, update availability, track performance).
- **FR-5:** Vehicle Management (Admin monitor fleet condition, mileage, and service history).
- **FR-6:** Delivery Assignment (Admin allocates pending requests to drivers & vehicles).
- **FR-7 & FR-8:** Driver Task Portal (Accept, reject, and update delivery milestones).
- **FR-9:** AI Cost Estimation (Instant pricing calculator factoring distance, weight, and vehicle type).
- **FR-10:** AI Predictive Maintenance & Notifications (Proactive fleet diagnostics & alerts).
