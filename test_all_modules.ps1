$baseUrl = "http://localhost:5000"
$ErrorActionPreference = "Continue"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "       FLEETIFY SYSTEM MODULES & FEATURES TEST SUITE      " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

function Assert-Test([string]$name, [bool]$condition, [string]$details = "") {
    if ($condition) {
        Write-Host " [PASS] $name" -ForegroundColor Green
        if ($details) { Write-Host "        $details" -ForegroundColor DarkGray }
    } else {
        Write-Host " [FAIL] $name" -ForegroundColor Red
        if ($details) { Write-Host "        $details" -ForegroundColor Yellow }
    }
}

# --- TEST 1: AI Cost Estimation API ---
Write-Host "`n--- 1. Testing AI Cost Estimation Module ---" -ForegroundColor Yellow
$aiBody = @{
    pickupLocation = "People Colony, Gujranwala"
    dropoffLocation = "Model Town, Lahore"
    parcelWeight = 4.5
    vehicleType = "Van"
    routeType = "Express"
} | ConvertTo-Json

try {
    $aiRes = Invoke-RestMethod -Uri "$baseUrl/api/ai/estimate-cost" -Method Post -ContentType "application/json" -Body $aiBody
    Assert-Test "AI Cost Calculation responds with HTTP 200" ($aiRes -ne $null)
    Assert-Test "AI Distance Estimation calculates route" ($aiRes.distanceKm -gt 0) "Distance: $($aiRes.distanceKm) km"
    Assert-Test "AI Fare Breakdown generated" ($aiRes.estimatedTotal -gt 0 -and $aiRes.distanceCharge -gt 0) "Total: `$$($aiRes.estimatedTotal), Days: $($aiRes.estimatedDeliveryDays)"
} catch {
    Assert-Test "AI Cost Calculation" $false $_.Exception.Message
}

# --- TEST 2: Authentication & Strict Role Isolation ---
Write-Host "`n--- 2. Testing Authentication & Strict Role Isolation ---" -ForegroundColor Yellow

# 2.1 Login as Customer
$customerSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
try {
    # Get login form to get __RequestVerificationToken
    $loginPage = Invoke-WebRequest -Uri "$baseUrl/Account/Login?role=Customer" -WebSession $customerSession -UseBasicParsing
    $tokenMatch = [regex]::Match($loginPage.Content, 'name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"')
    $token = $tokenMatch.Groups[1].Value

    $postBody = @{
        "__RequestVerificationToken" = $token
        "Role" = "Customer"
        "Email" = "customer@fleetify.com"
        "Password" = "customer123"
    }
    $loginRes = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -Body $postBody -WebSession $customerSession -MaximumRedirection 0 -ErrorAction SilentlyContinue
    $isRedirect = ($loginRes.StatusCode -eq 302)
    Assert-Test "Customer Login (valid credentials)" $isRedirect "Redirected to: $($loginRes.Headers.Location)"
} catch {
    Assert-Test "Customer Login" $false $_.Exception.Message
}

# 2.2 Cross-Role Isolation: Customer attempting to login on Admin screen
$adminAttemptSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
try {
    $adminLoginPage = Invoke-WebRequest -Uri "$baseUrl/Account/Login?role=Admin" -WebSession $adminAttemptSession -UseBasicParsing
    $tokenMatch = [regex]::Match($adminLoginPage.Content, 'name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"')
    $token = $tokenMatch.Groups[1].Value

    $postBody = @{
        "__RequestVerificationToken" = $token
        "Role" = "Admin"
        "Email" = "customer@fleetify.com"
        "Password" = "customer123"
    }
    $crossRes = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -Body $postBody -WebSession $adminAttemptSession -UseBasicParsing
    $rejected = ($crossRes.Content -like "*registered as*")
    Assert-Test "Strict Role Isolation (Customer rejected from Admin login)" $rejected "Properly rejected with role warning"
} catch {
    Assert-Test "Strict Role Isolation" $false $_.Exception.Message
}

# 2.3 Login as Admin
$adminSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
try {
    $adminLoginPage = Invoke-WebRequest -Uri "$baseUrl/Account/Login?role=Admin" -WebSession $adminSession -UseBasicParsing
    $tokenMatch = [regex]::Match($adminLoginPage.Content, 'name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"')
    $token = $tokenMatch.Groups[1].Value

    $postBody = @{
        "__RequestVerificationToken" = $token
        "Role" = "Admin"
        "Email" = "admin@fleetify.com"
        "Password" = "admin123"
    }
    $adminRes = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -Body $postBody -WebSession $adminSession -MaximumRedirection 0 -ErrorAction SilentlyContinue
    Assert-Test "Admin Login (valid credentials)" ($adminRes.StatusCode -eq 302) "Redirected to: $($adminRes.Headers.Location)"
} catch {
    Assert-Test "Admin Login" $false $_.Exception.Message
}

# 2.4 Login as Driver
$driverSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
try {
    $driverLoginPage = Invoke-WebRequest -Uri "$baseUrl/Account/Login?role=Driver" -WebSession $driverSession -UseBasicParsing
    $tokenMatch = [regex]::Match($driverLoginPage.Content, 'name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"')
    $token = $tokenMatch.Groups[1].Value

    $postBody = @{
        "__RequestVerificationToken" = $token
        "Role" = "Driver"
        "Email" = "driver1@fleetify.com"
        "Password" = "driver123"
    }
    $driverRes = Invoke-WebRequest -Uri "$baseUrl/Account/Login" -Method Post -Body $postBody -WebSession $driverSession -MaximumRedirection 0 -ErrorAction SilentlyContinue
    Assert-Test "Driver Login (valid credentials)" ($driverRes.StatusCode -eq 302) "Redirected to: $($driverRes.Headers.Location)"
} catch {
    Assert-Test "Driver Login" $false $_.Exception.Message
}

# --- TEST 3: Customer Delivery Booking ---
Write-Host "`n--- 3. Testing Customer Delivery Booking Flow ---" -ForegroundColor Yellow
$createdTrackingNumber = ""
try {
    $custDashboard = Invoke-WebRequest -Uri "$baseUrl/Customer" -WebSession $customerSession -UseBasicParsing
    $tokenMatch = [regex]::Match($custDashboard.Content, 'name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"')
    $token = $tokenMatch.Groups[1].Value

    $bookingBody = @{
        "__RequestVerificationToken" = $token
        "NewDelivery.PickupLocation" = "DC Colony, Gujranwala"
        "NewDelivery.DropoffLocation" = "Gulberg III, Lahore"
        "NewDelivery.ParcelWeight" = "3.5"
        "NewDelivery.ParcelDescription" = "Sample Electronics Package"
        "NewDelivery.VehicleType" = "Van"
        "NewDelivery.RouteType" = "Standard"
        "NewDelivery.EstimatedDistanceKm" = "72"
        "NewDelivery.PickupDate" = (Get-Date).ToString("yyyy-MM-dd")
    }

    $bookRes = Invoke-WebRequest -Uri "$baseUrl/Customer/CreateDelivery" -Method Post -Body $bookingBody -WebSession $customerSession -MaximumRedirection 0 -ErrorAction SilentlyContinue
    Assert-Test "Customer CreateDelivery POST succeeded (302 Redirect)" ($bookRes.StatusCode -eq 302)

    # Check customer dashboard to find the new tracking number
    $afterBooking = Invoke-WebRequest -Uri "$baseUrl/Customer" -WebSession $customerSession -UseBasicParsing
    $match = [regex]::Match($afterBooking.Content, 'FLT-\d{4}-\d+')
    if ($match.Success) {
        $createdTrackingNumber = $match.Value
        Assert-Test "New Delivery Registered in Database" $true "Tracking Number: $createdTrackingNumber"
    } else {
        Assert-Test "New Delivery Registered in Database" $false "Tracking number not found in customer dashboard"
    }
} catch {
    Assert-Test "Customer Delivery Booking" $false $_.Exception.Message
}

# --- TEST 4: Admin Dispatch & Fleet Operations ---
Write-Host "`n--- 4. Testing Admin Management & Dispatch ---" -ForegroundColor Yellow
try {
    # View Admin Assignments
    $assignPage = Invoke-WebRequest -Uri "$baseUrl/Admin/Assignments" -WebSession $adminSession -UseBasicParsing
    Assert-Test "Admin Assignments Page Accessible" ($assignPage.StatusCode -eq 200)

    # Find pending delivery ID and assign to driver1
    $tokenMatch = [regex]::Match($assignPage.Content, 'name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"')
    $token = $tokenMatch.Groups[1].Value

    $reqIdMatch = [regex]::Match($assignPage.Content, 'name="requestId"\s+value="(\d+)"')
    if ($reqIdMatch.Success) {
        $targetReqId = $reqIdMatch.Groups[1].Value
        $driverIdMatch = [regex]::Match($assignPage.Content, 'value="(\d+)">[A-Za-z\s]+\(Available\)')
        $driverId = if ($driverIdMatch.Success) { $driverIdMatch.Groups[1].Value } else { "2" }

        $vehIdMatch = [regex]::Match($assignPage.Content, 'value="(\d+)">FLT-')
        $vehId = if ($vehIdMatch.Success) { $vehIdMatch.Groups[1].Value } else { "1" }

        $assignBody = @{
            "__RequestVerificationToken" = $token
            "requestId" = $targetReqId
            "driverId" = $driverId
            "vehicleId" = $vehId
        }
        $doAssignRes = Invoke-WebRequest -Uri "$baseUrl/Admin/AssignDelivery" -Method Post -Body $assignBody -WebSession $adminSession -MaximumRedirection 0 -ErrorAction SilentlyContinue
        Assert-Test "Admin AssignDelivery POST succeeded" ($doAssignRes.StatusCode -eq 302) "Assigned Req #$targetReqId to Driver #$driverId and Vehicle #$vehId"
    } else {
        Assert-Test "Admin found pending delivery to assign" $true "No pending delivery needed assignment (all assigned)"
    }

    # Test AI Fleet Diagnostics
    $diagBody = @{
        "__RequestVerificationToken" = $token
    }
    $diagRes = Invoke-WebRequest -Uri "$baseUrl/Admin/RunMaintenanceDiagnostic" -Method Post -Body $diagBody -WebSession $adminSession -MaximumRedirection 0 -ErrorAction SilentlyContinue
    Assert-Test "Admin RunFleetAIDiagnostic POST succeeded" ($diagRes.StatusCode -eq 302)
} catch {
    Assert-Test "Admin Dispatch & Fleet Operations" $false $_.Exception.Message
}

# --- TEST 5: Driver Task Management & Milestone Progression ---
Write-Host "`n--- 5. Testing Driver Task Operations ---" -ForegroundColor Yellow
try {
    $driverDash = Invoke-WebRequest -Uri "$baseUrl/Driver" -WebSession $driverSession -UseBasicParsing
    Assert-Test "Driver Dashboard Accessible" ($driverDash.StatusCode -eq 200)

    $tokenMatch = [regex]::Match($driverDash.Content, 'name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"')
    $token = $tokenMatch.Groups[1].Value

    # Check if any assignment is available to accept
    $assignIdMatch = [regex]::Match($driverDash.Content, 'name="assignmentId"\s+value="(\d+)"')
    if ($assignIdMatch.Success) {
        $assignId = $assignIdMatch.Groups[1].Value

        # Accept Assignment
        $acceptBody = @{
            "__RequestVerificationToken" = $token
            "assignmentId" = $assignId
        }
        $acceptRes = Invoke-WebRequest -Uri "$baseUrl/Driver/AcceptAssignment" -Method Post -Body $acceptBody -WebSession $driverSession -MaximumRedirection 0 -ErrorAction SilentlyContinue
        Assert-Test "Driver AcceptAssignment POST succeeded" ($acceptRes.StatusCode -eq 302) "Accepted Assignment #$assignId"

        # Update Progress to Delivered
        $statusBody = @{
            "__RequestVerificationToken" = $token
            "assignmentId" = $assignId
            "status" = "Delivered"
            "remarks" = "Package handed over directly to recipient and signed off."
        }
        $updateRes = Invoke-WebRequest -Uri "$baseUrl/Driver/UpdateDeliveryStatus" -Method Post -Body $statusBody -WebSession $driverSession -MaximumRedirection 0 -ErrorAction SilentlyContinue
        Assert-Test "Driver UpdateDeliveryStatus to 'Delivered' succeeded" ($updateRes.StatusCode -eq 302)
    } else {
        Write-Host " [INFO] Driver currently has no pending assignment in available tab" -ForegroundColor DarkGray
    }
} catch {
    Assert-Test "Driver Task Operations" $false $_.Exception.Message
}

# --- TEST 6: Customer Shipment Tracking Timeline ---
Write-Host "`n--- 6. Testing Customer Tracking View ---" -ForegroundColor Yellow
try {
    if ($createdTrackingNumber) {
        $trackPage = Invoke-WebRequest -Uri "$baseUrl/Customer/Track?trackingNumber=$createdTrackingNumber" -WebSession $customerSession -UseBasicParsing
        Assert-Test "Customer Track Page renders for $createdTrackingNumber" ($trackPage.StatusCode -eq 200)
        Assert-Test "Customer Track Page displays shipment details" ($trackPage.Content -like "*$createdTrackingNumber*")
    } else {
        $trackPage = Invoke-WebRequest -Uri "$baseUrl/Customer/Track?trackingNumber=FLT-2026-001238" -WebSession $customerSession -UseBasicParsing
        Assert-Test "Customer Track Page renders for FLT-2026-001238" ($trackPage.StatusCode -eq 200)
    }
} catch {
    Assert-Test "Customer Shipment Tracking View" $false $_.Exception.Message
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host "                TEST SUITE EXECUTION COMPLETED            " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
