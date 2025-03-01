using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Samples.AWS.Lambda
{
    public class Function: BaseFunction
    {
        private static async Task Main(string[] args)
        {
            Console.WriteLine($"Is Tracer Attached: {SampleHelpers.IsProfilerAttached()}");
            Console.WriteLine($"Tracer location: {SampleHelpers.GetTracerAssemblyLocation()}");
            
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_NO_PARAM_SYNC"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_ONE_PARAM_SYNC"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_TWO_PARAMS_SYNC"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_NO_PARAM_ASYNC"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_ONE_PARAM_ASYNC"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_TWO_PARAMS_ASYNC"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_NO_PARAM_VOID"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_ONE_PARAM_VOID"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_TWO_PARAMS_VOID"));

            // with context
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_NO_PARAM_SYNC_WITH_CONTEXT"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_ONE_PARAM_SYNC_WITH_CONTEXT"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_TWO_PARAMS_SYNC_WITH_CONTEXT"));
            
            // base functions
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_BASE_NO_PARAM_SYNC"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_BASE_TWO_PARAMS_SYNC"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_BASE_ONE_PARAM_SYNC_WITH_CONTEXT"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_BASE_ONE_PARAM_ASYNC"));
            Thread.Sleep(1000);
            await Post(Environment.GetEnvironmentVariable("AWS_LAMBDA_ENDPOINT_BASE_TWO_PARAMS_VOID"));
        }
        private static async Task<string> Post(string url)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(url);
            client.DefaultRequestHeaders
                  .Accept
                  .Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("x-datadog-tracing-enabled", "false");

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/2015-03-31/functions/function/invocations");
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            HttpResponseMessage response = await client.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }

        public object HandlerNoParamSync()
        {
            Get("http://localhost/function/HandlerNoParamSync");
            return new { statusCode = 200, body = "ok!" };
        }

        public object HandlerOneParamSync(CustomInput request)
        {
            Get("http://localhost/function/HandlerOneParamSync");
            return new { statusCode = 200, body = "ok!" };
        }

        public object HandlerTwoParamsSync(CustomInput request, ILambdaContext context)
        {
            Get("http://localhost/function/HandlerTwoParamsSync");
            return new { statusCode = 200, body = "ok!" };
        }

        public object HandlerNoParamSyncWithContext()
        {
            Get("http://localhost/function/HandlerNoParamSyncWithContext");
            return new { statusCode = 200, body = "ok!" };
        }

        public object HandlerOneParamSyncWithContext(CustomInput request)
        {
            Get("http://localhost/function/HandlerOneParamSyncWithContext");
            return new { statusCode = 200, body = "ok!" };
        }

        public object HandlerTwoParamsSyncWithContext(CustomInput request, ILambdaContext context)
        {
            Get("http://localhost/function/HandlerTwoParamsSyncWithContext");
            return new { statusCode = 200, body = "ok!" };
        }

        public async Task<int> HandlerNoParamAsync()
        {
            await Task.Run(() => {
                Get("http://localhost/function/HandlerNoParamAsync");
                Thread.Sleep(100);
            });
            return 10;
        }

        public async Task<int> HandlerOneParamAsync(CustomInput request)
        {
            await Task.Run(() => {
                Get("http://localhost/function/HandlerOneParamAsync");
                Thread.Sleep(100);
            });
            return 10;
        }

        public async Task<int> HandlerTwoParamsAsync(CustomInput request, ILambdaContext context)
        {
            await Task.Run(() => {
                Get("http://localhost/function/HandlerTwoParamsAsync");
                Thread.Sleep(100);
            });
            return 10;
        }

        public void HandlerNoParamVoid()
        {
            Get("http://localhost/function/HandlerNoParamVoid");
        }

        public void HandlerOneParamVoid(CustomInput request)
        {
            Get("http://localhost/function/HandlerOneParamVoid");
        }

        public void HandlerTwoParamsVoid(CustomInput request, ILambdaContext context)
        {
            Get("http://localhost/function/HandlerTwoParamsVoid");
        }
    }

    public class CustomInput
    {
        public string Field1 { get; set; }
        public int Field2 { get; set; }
    }

    public class BaseFunction
    {
        public object BaseHandlerNoParamSync()
        {
            Get("http://localhost/function/BaseHandlerNoParamSync");
            return new { statusCode = 200, body = "ok!" };
        }

        public object BaseHandlerTwoParamsSync(CustomInput request, ILambdaContext context)
        {
            Get("http://localhost/function/BaseHandlerTwoParamsSync");
            return new { statusCode = 200, body = "ok!" };
        }

        public object BaseHandlerOneParamSyncWithContext(CustomInput request)
        {
            Get("http://localhost/function/BaseHandlerOneParamSyncWithContext");
            return new { statusCode = 200, body = "ok!" };
        }

        public async Task<int> BaseHandlerOneParamAsync(CustomInput request)
        {
            await Task.Run(() => {
                Get("http://localhost/function/BaseHandlerOneParamAsync");
                Thread.Sleep(100);
            });
            return 10;
        }

        public void BaseHandlerTwoParamsVoid(CustomInput request, ILambdaContext context)
        {
            Get("http://localhost/function/BaseHandlerTwoParamsVoid");
        }

        protected void Get(string url)
        {
            WebRequest request = WebRequest.Create(url);
            request.Credentials = CredentialCache.DefaultCredentials;
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
            using (Stream dataStream = response.GetResponseStream())
            {
                StreamReader reader = new StreamReader(dataStream);
                reader.ReadToEnd();
            }
            response.Close();
        }
    }
}
