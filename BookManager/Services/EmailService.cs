using System.Net;
using System.Net.Mail;
using System.Text;

public interface IEmailService
{
    void SendEmail(string to, string subject, string body);
}

public class EmailService : IEmailService
{
    public void SendEmail(string to, string subject, string body)
    {
        using (var client = new SmtpClient("smtp.gmail.com"))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential("holynotifier420@gmail.com", "ncxecsdfeltyswxh");
            client.Port = 587;
            client.EnableSsl = true;

            using (var message = new MailMessage("holynotifier420@gmail.com", to))
            {
                message.Subject = subject;
                message.SubjectEncoding = Encoding.UTF8;
                message.Body = body;
                message.BodyEncoding = Encoding.UTF8;
                message.IsBodyHtml = true;

                client.Send(message);
            }
        }
    }
}
