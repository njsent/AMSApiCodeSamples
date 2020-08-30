using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace AMS.Api.Code
{
    public class CSSyncSample
    {
        string userName;
        string password;

        public CSSyncSample(string userName, string password, string host = "https://api.ams.aamanet.org")
        {
            this.userName = userName;
            this.password = password;
        }

        public async Task Synchronize()
        {
            // fetch our lastUpdateDate from our data store
            var lastUpdateDate = GetLastUpdateDateFromStore();
            // fetch the latest organizations and do something awesome such as posting to your data store
            var organizations = await SyncOrganizations(lastUpdateDate);

            // update our lastUpdateDate for next time if we received any results
            if (organizations.Any()) {
                // get the maximum updatedDate from our sync process
                lastUpdateDate = organizations.Max(o => o.UpdatedDate);
                StoreLastUpdateDate(lastUpdateDate.Value);

                Console.WriteLine("Organizations Received:");
                // lets serialize the first 5 it so that we can display the results  (NOTE that our serializer (System.Text.Json) is encoding certain characters such as '&')
                Console.WriteLine(JsonSerializer.Serialize(organizations.Take(5), new JsonSerializerOptions() { WriteIndented = true }));

            }
            else
                Console.WriteLine("Oh no! There were no matching records.  Try backdating the lastUpdateDate a month or so");

            Console.ReadLine();

        }

        /// <summary>
        /// Synchronize Organizations by passing the maximum updatedDate received on our previous request.  If this is our first sync, updatedDate will be null
        /// </summary>
        /// <param name="lastUpdateDate"></param>
        /// <returns></returns>
        public async Task<List<Organization>> SyncOrganizations(DateTime? lastUpdateDate)
        {
            bool complete = false;
            // odata query options
            int top = 10; int skip = 0; string filter = "";
            int page = 0;
            // if we received 
            if (lastUpdateDate.HasValue)
                filter = $"&$filter=updatedDate gt {lastUpdateDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ")}";

            var organizations = new List<Organization>();

            using (var client = CreateHttpClient()) {
                while (!complete) {
                    // set our odata query 
                    var uri = $"{host}/Organizations?$select=orgId,orgName,orgDescription,mailingAddress,phone,orgType,status,hasLocations,updatedDate&$count=true&$top={top}&$skip={skip}{filter}";

                    // query the endpoint and deserialize the results
                    var response = await QueryAndDeserialize<Organization>(client, uri);

                    // append the results to our organizations list
                    if (response.Value.Any()) {
                        // for sake of this example, let display the total number of records that match our criteria
                        if (!organizations.Any())
                            Console.WriteLine($"Total number of Organizations matching our $filter: {response.Count}");

                        organizations.AddRange(response.Value);
                    }

                    /*  check the size of the array we just received which is contained in the Value property, if it is less than the amount of  
                     *  records we are requesting (from top variable) then we are finished receiving records
                     */

                    if (response.Value.Length < top)
                        complete = true;
                    else {
                        // increment the page and multiply by how many records we are taking 
                        skip = ++page * top;
                    }
                }
            }

            return organizations;
        }

        /// <summary>
        /// Helper method to create http client capable of decompressing response
        /// </summary>
        /// <param name="scheme"></param>
        /// <param name="parameter"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        protected HttpClient CreateHttpClient()
        {
            try {
                var handler = new HttpClientHandler() {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                };

                var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };

                // create basic authentication header using Base64 encoding
                var credentials = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(userName + ":" + password));

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials);

                return client;

            }
            catch (Exception ex) {
                throw ex;
            }
        }

        /// <summary>
        /// Generic method to deserialize odata response contained in "value" property
        /// </summary>
        /// <typeparam name="T">Class which we are deserializing</typeparam>
        /// <param name="uri"></param>
        /// <returns></returns>
        public async Task<AMSResponse<T>> QueryAndDeserialize<T>(HttpClient client, string uri) where T : class
        {
            using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)) {

                // ensure our request was successful
                response.EnsureSuccessStatusCode();

                // get the content stream
                var contentStream = await response.Content.ReadAsStreamAsync();
                // set our json options to deserialize as case-insensitive since the incoming stream is camelCase and our properties are PascalCase
                var jsonOptions = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };

                return await JsonSerializer.DeserializeAsync<AMSResponse<T>>(contentStream, jsonOptions);
            }
        }

        DateTime? GetLastUpdateDateFromStore()
        {
            // here we would fetch the last update date (UTC) from our data store, in this case lets just get everything from the last month
            return DateTime.UtcNow.AddMonths(-1);
        }

        void StoreLastUpdateDate(DateTime lastUpdateDate)
        {
            // here we would store the maximum 'updatedDate' from our latest sync
        }

    }

    /// <summary>
    /// Create a generic class so we can reuse this on other resources (Individuals, Locations, etc.)
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class AMSResponse<T>
    {
        [JsonPropertyName("@odata.count")]
        public int Count { get; set; }  // if we supply the $count odata query option, this will be the total number of records which match our $filter criteria

        [JsonPropertyName("value")]
        public T[] Value { get; set; }  // the response received from our request containing an array of T
    }


    public class Organization
    {
        public int OrgId { get; set; }
        public string OrgName { get; set; }
        public string OrgDescription { get; set; }
        public Address MailingAddress { get; set; }
        public string Phone { get; set; }
        public string OrgType { get; set; }
        public string Status { get; set; }
        public bool HasLocations { get; set; }
        public DateTime UpdatedDate { get; set; }
    }

    public class Address
    {
        public int AddressId { get; set; }
        public string Address1 { get; set; }
        public string Address2 { get; set; }
        public string City { get; set; }
        public string StateCode { get; set; }
        public string Zip { get; set; }
        public string CountryCode { get; set; }

    }
}
