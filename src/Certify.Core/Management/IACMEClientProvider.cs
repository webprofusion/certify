namespace Certify.Management
{
    public interface IACMEClientProvider
    {
        bool AddNewRegistrationAndAcceptTOS(string email);

        string GetAcmeBaseURI();
    }
}