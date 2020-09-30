using System;
using System.Threading.Tasks;
using BaGet.Core.Configuration;
using Microsoft.Extensions.Options;

namespace BaGet.Core.Authentication
{
    public class ApiKeyAuthenticationService : IAuthenticationService
    {
        private readonly string _apiKey;

        public ApiKeyAuthenticationService(IOptionsSnapshot<BaGetOptions> options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));

            _apiKey = string.IsNullOrEmpty(options.Value.MasterKey) ? null : options.Value.MasterKey;
        }

        public Task<bool> AuthenticateAsync(string apiKey, string packageKey) => Task.FromResult(Authenticate(apiKey, packageKey));

        public bool Authenticate(string apiKey, string packageKey)
        {
            // Check Master Key
            if (_apiKey == apiKey)
                return true;

            // Check Optional Package Key
            if (!string.IsNullOrEmpty(packageKey))
                return apiKey == packageKey;

            // Authenticate if neither is set.
            return true;
        }
    }
}
