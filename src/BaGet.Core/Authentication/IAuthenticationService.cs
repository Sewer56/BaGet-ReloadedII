using System.Threading.Tasks;

namespace BaGet.Core.Authentication
{
    public interface IAuthenticationService
    {
        Task<bool> AuthenticateAsync(string apiKey, string packageKey);
        bool Authenticate(string apiKey, string packageKey);
    }
}
