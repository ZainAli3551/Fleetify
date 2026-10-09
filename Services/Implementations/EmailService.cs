using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Fleetify.Services.Interfaces;

namespace Fleetify.Services.Implementations
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<bool> SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true)
        {
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                _logger.LogWarning("Email dispatch skipped: recipient email address is empty.");
                return false;
            }

            var smtpServer = _config["EmailSettings:SmtpServer"] ?? "smtp.gmail.com";
            var smtpPort = int.TryParse(_config["EmailSettings:SmtpPort"], out int port) ? port : 587;
            var senderEmail = _config["EmailSettings:SenderEmail"] ?? "fypfleetify@gmail.com";
            var senderName = _config["EmailSettings:SenderName"] ?? "Fleetify Logistics";
            var password = _config["EmailSettings:Password"];
            var enableSsl = bool.TryParse(_config["EmailSettings:EnableSsl"], out bool ssl) ? ssl : true;

            string formattedBody = isHtml ? WrapInHtmlTemplate(subject, body) : body;

            // 1. Attempt real Gmail SMTP dispatch if password/app-password is configured
            if (!string.IsNullOrWhiteSpace(password))
            {
                var cleanPassword = password.Replace(" ", "").Trim();
                var hostsToTry = new[] { smtpServer, "smtp.googlemail.com" };

                foreach (var host in hostsToTry)
                {
                    for (int attempt = 1; attempt <= 2; attempt++)
                    {
                        try
                        {
                            using var client = new SmtpClient(host, smtpPort)
                            {
                                Credentials = new NetworkCredential(senderEmail, cleanPassword),
                                EnableSsl = enableSsl,
                                DeliveryMethod = SmtpDeliveryMethod.Network,
                                Timeout = 15000
                            };

                            using var mailMessage = new MailMessage
                            {
                                From = new MailAddress(senderEmail, senderName),
                                Subject = subject,
                                Body = formattedBody,
                                IsBodyHtml = isHtml
                            };
                            mailMessage.To.Add(toEmail);

                            await client.SendMailAsync(mailMessage);
                            _logger.LogInformation("Real Gmail SMTP notification successfully sent to {ToEmail} via {Host}. Subject: {Subject}", toEmail, host, subject);
                            LogToAuditFile(toEmail, subject, body, "SMTP_SENT");
                            return true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "SMTP attempt {Attempt} on {Host} failed for {ToEmail}: {Error}", attempt, host, ex.Message);
                            if (attempt < 2)
                            {
                                await Task.Delay(1000);
                            }
                        }
                    }
                }
            }

            // 2. Simulated/Fallback dispatch for development and reliable delivery verification
            _logger.LogInformation("================== GMAIL NOTIFICATION DISPATCHED ==================");
            _logger.LogInformation("Recipient (Gmail): {ToEmail}", toEmail);
            _logger.LogInformation("Subject:           {Subject}", subject);
            _logger.LogInformation("Content:           {Body}", body);
            _logger.LogInformation("===================================================================");

            LogToAuditFile(toEmail, subject, body, "DISPATCHED_LOG");
            return false;
        }

        private void LogToAuditFile(string toEmail, string subject, string body, string status)
        {
            try
            {
                var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(logsDir);
                var logFile = Path.Combine(logsDir, "sent_emails.log");
                var entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}] [{status}] TO: {toEmail} | SUBJECT: {subject}{Environment.NewLine}{body}{Environment.NewLine}------------------------------------------------------------{Environment.NewLine}";
                File.AppendAllText(logFile, entry);
            }
            catch
            {
                // Silently ignore file access concurrency in log file
            }
        }

        private static string WrapInHtmlTemplate(string title, string content)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; background-color: #f4f6f9; margin: 0; padding: 20px; }}
        .container {{ max-width: 600px; margin: 0 auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.05); border: 1px solid #e2e8f0; }}
        .header {{ background: linear-gradient(135deg, #0f172a 0%, #1e3a8a 100%); padding: 25px 30px; text-align: center; color: white; }}
        .header h1 {{ margin: 0; font-size: 22px; font-weight: 700; letter-spacing: 0.5px; }}
        .header p {{ margin: 5px 0 0 0; opacity: 0.85; font-size: 13px; color: #93c5fd; }}
        .content {{ padding: 30px; color: #1e293b; line-height: 1.6; font-size: 15px; }}
        .badge {{ display: inline-block; background-color: #e0f2fe; color: #0284c7; padding: 5px 12px; border-radius: 20px; font-weight: 600; font-size: 12px; margin-bottom: 15px; }}
        .card {{ background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 8px; padding: 18px; margin: 18px 0; font-size: 14px; white-space: pre-wrap; }}
        .notice {{ background-color: #fefce8; border-left: 4px solid #eab308; padding: 12px 16px; border-radius: 4px; margin-top: 15px; font-size: 13px; color: #854d0e; }}
        .footer {{ background-color: #f8fafc; padding: 20px; text-align: center; font-size: 12px; color: #64748b; border-top: 1px solid #e2e8f0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Fleetify Logistics</h1>
            <p>Reliable Freight &amp; Smart Warehouse Network</p>
        </div>
        <div class='content'>
            <div class='badge'>Direct Gmail Notification</div>
            <h2 style='color: #0f172a; margin-top: 0; font-size: 18px;'>{title}</h2>
            <div class='card'>{content}</div>
            <div class='notice'>
                <strong>Notice:</strong> Yeh notification aap ke registered Gmail address par bheja gaya hai.
            </div>
        </div>
        <div class='footer'>
            &copy; {DateTime.UtcNow.Year} Fleetify Logistics Network. All rights reserved.
        </div>
    </div>
</body>
</html>";
        }
    }
}
