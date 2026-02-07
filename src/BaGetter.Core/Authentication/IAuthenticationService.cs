using System.Threading;
using System.Threading.Tasks;

namespace BaGetter.Core;

public interface IAuthenticationService
{
    Task<bool> AuthenticateAsync(string apiKey, CancellationToken cancellationToken);
    Task<bool> AuthenticateAsync(string apiKey, string packageKey, CancellationToken cancellationToken);
}
