using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Certify.Models.Plugins;
using Certify.Models.Shared;
using Newtonsoft.Json;

namespace Certify.Providers.Internal
{
    public class DashboardClient : IDashboardClient
    {
        private readonly HttpClient _client;

        public DashboardClient()
        {
            _client = new HttpClient();
        }

        private async Task<HttpResponseMessage> PostAsync(string endpoint, object data)
        {
            var _baseURI = Models.API.Config.APIBaseURI;

            if (data != null)
            {
                var json = JsonConvert.SerializeObject(data);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                return await _client.PostAsync(_baseURI + endpoint, content);
            }
            else
            {
                return await _client.PostAsync(_baseURI + endpoint, new StringContent(""));
            }
        }

        public async Task<bool> ReportRenewalStatusAsync(RenewalStatusReport report)
        {
            try
            {
                var response = await PostAsync("status/submit", report);
                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> ReportUserActionRequiredAsync(ItemActionRequired actionRequired)
        {
            try
            {
                var response = await PostAsync("status/actionRequired", actionRequired);

                Debug.WriteLine(JsonConvert.SerializeObject(response));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public Task<bool> ReportServerStatusAsync()
        {
            throw new NotImplementedException();
        }

        public Task<bool> SignInAsync(string email, string pwd)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> SubmitFeedbackAsync(FeedbackReport feedback, string frameworkVersion)
        {
            //submit feedback if connection available

            feedback.SupportingData = new
            {
                Framework = frameworkVersion,
                OS = Environment.OSVersion.ToString(),
                feedback.IsException,
                feedback.AppVersion
            };

            try
            {
                var response = await PostAsync("feedback/submit", feedback);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (Exception exp)
            {
                Debug.WriteLine(exp.ToString());
            }

            return false;
        }

        public async Task<bool> RegisterInstance(RegisteredInstance instance, string email, string pwd, bool createAccount)
        {
            try
            {
                //_baseUri = "http://localhost:57248/v1/";
                var response = await PostAsync("status/register", new
                {
                    Instance = instance,
                    Email = email,
                    Password = pwd,
                    CreateAccount = createAccount
                });

                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
