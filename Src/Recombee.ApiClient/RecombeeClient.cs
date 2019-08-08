using System;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Recombee.ApiClient.ApiRequests;
using Recombee.ApiClient.Bindings;

namespace Recombee.ApiClient
{
    /// <summary>Client for sending requests to Recombee and getting replies</summary>
    public partial class RecombeeClient
    {
        readonly string databaseId;
        readonly byte[] secretTokenBytes;

        readonly bool useHttpsAsDefault;
        readonly bool isClient;
        private readonly HttpMessageHandler handler;

        readonly string hostUri = "rapi.recombee.com";
        readonly string clientHostUri = "client-rapi.recombee.com";

        readonly int BATCH_MAX_SIZE = 10000; //Maximal number of requests within one batch request

        HttpClient httpClient;


        /// <summary>Initialize the client</summary>
        /// <param name="databaseId">ID of the database.</param>
        /// <param name="secretToken">Corresponding secret token.</param>
        /// <param name="useHttpsAsDefault">If true, all requests are sent using HTTPS</param>
        /// <param name="isClient">If true, access the API using client->server communication with the public token as the secretToken instead of using server->server communication with the private token</param>
        /// <param name="handler">Optional message handler for the http client</param>
        public RecombeeClient(string databaseId, string secretToken, bool useHttpsAsDefault = true, bool isClient = false, HttpMessageHandler handler = null)
        {
            this.databaseId = databaseId;
            this.secretTokenBytes = Encoding.ASCII.GetBytes(secretToken);
            this.useHttpsAsDefault = useHttpsAsDefault;
            this.isClient = isClient;
            this.handler = handler;
            this.httpClient = createHttpClient();

            var envHostUri = Environment.GetEnvironmentVariable("RAPI_URI");
            if (envHostUri != null)
            {
                this.hostUri = envHostUri;
                this.clientHostUri = envHostUri;
            }
        }

        private HttpClient createHttpClient()
        {
            var httpClient = handler == null ? new HttpClient() : new HttpClient(handler);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "recombee-.net-api-client/2.4.1");
            return httpClient;
        }

        public StringBinding Send(Request request)
        {
            var json = SendRequest(request);
            return ParseResponse(json, request);
        }

        public StringBinding ParseResponse(string json, Request request)
        {
            return new StringBinding(json);
        }

        public IEnumerable<Item> Send(ListItems request)
        {
            var json = SendRequest(request);
            return ParseResponse(json, request);
        }

        public IEnumerable<User> Send(ListUsers request)
        {
            var json = SendRequest(request);
            return ParseResponse(json, request);
        }

        public IEnumerable<Recommendation> Send(UserBasedRecommendation request)
        {
            var json = SendRequest(request);
            return ParseResponse(json, request);
        }

        public IEnumerable<Recommendation> Send(ItemBasedRecommendation request)
        {
            var json = SendRequest(request);
            return ParseResponse(json, request);
        }

        private IEnumerable<Recommendation> ParseResponse(string json, UserBasedRecommendation request)
        {
            return ParseRecommRequestResponse(json, request);
        }

        private IEnumerable<Recommendation> ParseResponse(string json, ItemBasedRecommendation request)
        {
            return ParseRecommRequestResponse(json, request);
        }

        private IEnumerable<Recommendation> ParseRecommRequestResponse(string json, Request request)
        {
            try
            {
                var strArray = JsonConvert.DeserializeObject<string[]>(json);
                return strArray.Select(x => new Recommendation(x));
            }
            catch(Newtonsoft.Json.JsonReaderException)
            {
                //might have failed because it returned also the item properties
                var valsArray = JsonConvert.DeserializeObject<Dictionary<string, object>[]>(json);
                return valsArray.Select(vals => new Recommendation((string)vals["itemId"], vals));
            }
        }

        private IEnumerable<Item> ParseResponse(string json, ListItems request)
        {
            try
            {
                var strArray = JsonConvert.DeserializeObject<string[]>(json);
                return strArray.Select(x => new Item(x));
            }
            catch(Newtonsoft.Json.JsonReaderException)
            {
                //might have failed because it returned also the item properties
                var valsArray = JsonConvert.DeserializeObject<Dictionary<string, object>[]>(json);
                return valsArray.Select(vals => new Item((string)vals["itemId"], vals));
            }
        }

        private IEnumerable<User> ParseResponse(string json, ListUsers request)
        {
            try
            {
                var strArray = JsonConvert.DeserializeObject<string[]>(json);
                return strArray.Select(x => new User(x));
            }
            catch(Newtonsoft.Json.JsonReaderException)
            {
                //might have failed because it returned also the item properties
                var valsArray = JsonConvert.DeserializeObject<Dictionary<string, object>[]>(json);
                return valsArray.Select(vals => new User((string)vals["userId"], vals));
            }
        }

        public Item Send(GetItemValues request)
        {
            var json = SendRequest(request);
            return ParseResponse(json, request);
        }

        private Item ParseResponse(string json, GetItemValues request)
        {
            var vals = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            return new Item(request.ItemId, vals);
        }

        public User Send(GetUserValues request)
        {
            var json = SendRequest(request);
            return ParseResponse(json, request);
        }

        private User ParseResponse(string json, GetUserValues request)
        {
            var vals = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            return new User(request.UserId, vals);
        }

        private class BatchParseHelper
        {
            public int code;
            public Newtonsoft.Json.Linq.JRaw json;
            
            public BatchParseHelper()
            {
                code = 0;
                json = null;
            }
        }
        public BatchResponse Send(Batch request)
        {
            if(request.Requests.Count() > BATCH_MAX_SIZE )
                return SendMultipartBatchRequest(request);

            var json = SendRequest(request);
            var partiallyParsed = JsonConvert.DeserializeObject<BatchParseHelper[]>(json);

            var resps = request.Requests.Zip(partiallyParsed, (req, res) => (object) ParseOneBatchResponse(res.json.ToString(), res.code, req));
            var statusCodes = partiallyParsed.Select(res => (HttpStatusCode) res.code);
            return new BatchResponse(resps, statusCodes);
        }

        private BatchResponse SendMultipartBatchRequest(Batch request)
        {
            var parts = request.Requests.Part(BATCH_MAX_SIZE);
            var results = parts.Select(reqs => Send(new Batch(reqs)));
            var responses = results.Select(br => br.Responses).SelectMany(x => x);
            var statusCodes = results.Select(br => br.StatusCodes).SelectMany(x => x);
            return new BatchResponse(responses, statusCodes);
        }


        protected string SendRequest(Request request)
        {
            var uri = ProcessRequestUri(request);
            try
            {
                HttpResponseMessage response = PerformHTTPRequest(uri, request);
                var jsonStringTask = response.Content.ReadAsStringAsync();
                jsonStringTask.Wait();
                var jsonString = jsonStringTask.Result;
                CheckStatusCode(response.StatusCode, jsonString, request);
                return jsonString;
            }
            catch(AggregateException ae)
            {
                ae.Handle((x) =>
                {
                    if(x is System.Threading.Tasks.TaskCanceledException)
                        throw new TimeoutException(request, x);
                    return false;
                });
            }
            throw new InvalidOperationException("Invalid state after sending a request."); //Should never happen
        }

        private void CheckStatusCode(System.Net.HttpStatusCode statusCode, string response, Request request)
        {
            int code = (int) statusCode;
            if(code>=200 && code <=299)
                return;
                
            throw new ResponseException(request, statusCode, response);
        }


        private HttpResponseMessage PerformHTTPRequest(string uri, Request request)
        {
            if (httpClient.Timeout != request.Timeout)
            {
                if (httpClient.Timeout != null)
                    httpClient = createHttpClient();
                httpClient.Timeout = request.Timeout;
            }
            
            if (request.RequestHttpMehod == HttpMethod.Get)
            {
                return httpClient.GetAsync(uri).Result;
            }
            else if (request.RequestHttpMehod == HttpMethod.Put)
            {
                return httpClient.PutAsync(uri, new StringContent("")).Result;
            }
            else if (request.RequestHttpMehod == HttpMethod.Post)
            {
                string bodyParams = JsonConvert.SerializeObject(request.BodyParameters());
                return httpClient.PostAsync(uri, new StringContent(bodyParams, Encoding.UTF8,"application/json")).Result;
            }
            else if (request.RequestHttpMehod == HttpMethod.Delete)
            {
                return httpClient.DeleteAsync(uri).Result;
            }
            else
            {
                throw new InvalidOperationException("Unknown method " + request.RequestHttpMehod.ToString());
            }
        }

        private string StripLeadingQuestionMark(string query)
        {
            if (query.Length > 0 && query[0] == '?')
            {
                return query.Substring(1);
            }
            return query;
        }

        private string ProcessRequestUri(Request request)
        {
            var uriBuilder = new UriBuilder();
            uriBuilder.Path = string.Format("/{0}{1}",databaseId,request.Path());
            uriBuilder.Query = QueryParameters(request);
            AppendHmacParameters(uriBuilder);
            uriBuilder.Scheme = (request.EnsureHttps || useHttpsAsDefault) ? "https" : "http";
            uriBuilder.Host = isClient ? clientHostUri : hostUri;
            return uriBuilder.ToString();
        }

        private string QueryParameters(Request request)
        {
            var encodedQueryStringParams = request.QueryParameters().
                Select (p => string.Format("{0}={1}", p.Key, WebUtility.UrlEncode(String.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", p.Value))));
            return string.Join("&", encodedQueryStringParams);
        }

        private Int32 UnixTimestampNow()
        {
            return (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
        }
        private void AppendHmacParameters(UriBuilder uriBuilder)
        {
            string timestampParam = isClient ? "frontend_timestamp" : "hmac_timestamp";
            string signParam = isClient ? "frontend_sign" : "hmac_sign";

            uriBuilder.Query = StripLeadingQuestionMark(uriBuilder.Query) + (uriBuilder.Query.Length == 0 ? "" : "&") + string.Format("{0}={1}",timestampParam,this.UnixTimestampNow());
            using(var myhmacsha1 = new HMACSHA1(this.secretTokenBytes))
            {
                var uriToBeSigned = uriBuilder.Path + uriBuilder.Query;
                byte[] hmacBytes = myhmacsha1.ComputeHash(Encoding.ASCII.GetBytes(uriToBeSigned));
                string hmacRes = BitConverter.ToString(hmacBytes).Replace("-","");
                uriBuilder.Query = StripLeadingQuestionMark(uriBuilder.Query) + string.Format("&{0}={1}",signParam,hmacRes);
            }
        }
    }
}
