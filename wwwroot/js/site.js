// Fleetify Global JavaScript Helper
document.addEventListener("DOMContentLoaded", function () {
    // Auto dismiss alerts after 5 seconds
    const alerts = document.querySelectorAll('.alert-dismissible');
    alerts.forEach(function (alert) {
        setTimeout(function () {
            try {
                const bsAlert = new bootstrap.Alert(alert);
                bsAlert.close();
            } catch (e) { }
        }, 6000);
    });
});
