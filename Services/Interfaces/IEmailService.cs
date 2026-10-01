using System.Threading.Tasks;

namespace Fleetify.Services.Interfaces
{
    public interface IEmailService
    {
        /// <summary>
        /// Sends an email notification to the recipient (e.g. Gmail).
        /// </summary>
        /// <param name="toEmail">Recipient's Gmail or email address</param>
        /// <param name="subject">Email subject</param>
        /// <param name="body">Email message content</param>
        /// <param name="isHtml">Whether body is HTML</param>
        /// <returns>True if dispatched successfully</returns>
        Task<bool> SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true);
    }
}
