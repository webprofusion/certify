using System.Threading.Tasks;

namespace Certify.Models.Providers
{
    public interface IDnsProvider
    {
        Task<bool> CreateRecord(string recordName, string recordValue);

        Task<bool> DeleteRecord(string recordName);
    }
}