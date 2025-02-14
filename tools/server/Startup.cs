//
// Copyright (c) Microsoft Corporation. All rights reserved.
// SPDX-License-Identifier: Apache-2.0
//
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace CommonSchema
{
    namespace Server
    {
        public class Startup
        {
            private int seq = 0;
            private static object locker = new Object();

            public Startup(IConfiguration configuration)
            {
                Configuration = configuration;
            }

            public IConfiguration Configuration { get; }

            // This method gets called by the runtime. Use this method to add services to the container.
            // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
            public void ConfigureServices(/* IServiceCollection services */)
            {
                // TODO: add services configuration
            }

            // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
            public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
            {
                if (env.IsDevelopment())
                {
                    app.UseDeveloperExceptionPage();
                }

                app.UseStaticFiles(); // For the wwwroot folder
                app.Run(async (context) =>
                {
                    // Dump request information with headers
                    seq++;
                    ILogger requestLogger = loggerFactory.CreateLogger("REQ-" + seq);
                    ILogger decoderLogger = loggerFactory.CreateLogger("DEC-" + seq);

                    string headersStr = "";
                    foreach (var entry in context.Request.Headers)
                    {
                        headersStr += entry.Key + ": " + entry.Value.ToString() + "\n";
                    }
                    requestLogger.LogInformation(headersStr);

                    try
                    {
                        string path = context.Request.Path.Value;
                        if (path.StartsWith("/OneCollectorWriteToFile"))
                        {
                            string result = DecodeTelemetryRequest(context, decoderLogger);
                            var fileName = Configuration.GetSection("FileNameToStoreTelemetryData")?.Value;
                            if (!string.IsNullOrEmpty(fileName))
                            {
                                lock (locker) 
                                {
                                    if (File.Exists(fileName))
                                    {
                                        var formattedResult = result.Replace("[", "").Replace("]", "");
                                        var currentContent = File.ReadAllText(fileName);
                                        var updatedContent = string.Concat(currentContent.Replace("]", ","), formattedResult, "]");
                                        File.WriteAllText(fileName, updatedContent);
                                    }
                                    else
                                    {
                                        File.AppendAllText(fileName, result);
                                    }
                                }
                            }
                            else
                            {
                                requestLogger.LogError("Parameter FileNameToStoreTelemetryData from appsettings.json, where data should be stored is not specified. As a result telemetry data won't be saved to file, but will be visible in the console");
                            }

                            // Echo the body converted to JSON array
                            context.Response.StatusCode = 200;
                            requestLogger.LogInformation(result);
                            await context.Response.WriteAsync(result);
                        }
                        else
                        if (path.StartsWith("/OneCollector/"))
                        {
                            string result = DecodeTelemetryRequest(context, decoderLogger);

                            // Echo the body converted to JSON array
                            context.Response.StatusCode = 200;
                            requestLogger.LogInformation(result);
                            await context.Response.WriteAsync(result);
                        }
                        else
                        /* Azure Monitor / Application Insights -compatible server */
                        if (path.StartsWith("/v2/track"))
                        {
                            int length = Int32.Parse(context.Request.Headers["Content-Length"]);
                            BinaryReader reader = new BinaryReader(context.Request.Body);

                            // Read body fully before decoding it
                            byte[] buffer = reader.ReadBytes(length);

                            if (context.Request.Headers["Content-Encoding"] == "gzip")
                            {
                                buffer = Decoder.Gunzip(buffer);
                            }
                            else
                            if (context.Request.Headers["Content-Encoding"] == "deflate")
                            {
                                buffer = Decoder.Inflate(buffer);
                            }

                            string result = System.Text.Encoding.UTF8.GetString(buffer);

                            // Echo the body converted to JSON array
                            context.Response.StatusCode = 200;
                            requestLogger.LogInformation(result);
                            await context.Response.WriteAsync(result);
                        }
                        else
                        if (path.StartsWith("/admin/stop"))
                        {
                            // Stop web-server
                            requestLogger.LogInformation("Stopping web-server...");
                            context.Response.StatusCode = 200;
                            await context.Response.WriteAsync("Server stopped.");
                            Environment.Exit(0);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Handle all error conditions here
                        string result = "400 Bad Request";
                        context.Response.StatusCode = 400;
                        requestLogger.LogError("Exception: {ex}", ex);
                        await context.Response.WriteAsync(result);
                    }
                });

            }

            private static string DecodeTelemetryRequest(HttpContext context, ILogger decoderLogger)
            {
                int length = Int32.Parse(context.Request.Headers["Content-Length"]);
                BinaryReader reader = new BinaryReader(context.Request.Body);

                // Read body fully before decoding it
                byte[] buffer = reader.ReadBytes(length);

                Dictionary<string, string> headers = new Dictionary<string, string>();
                foreach (KeyValuePair<string, StringValues> entry in context.Request.Headers)
                {
                    // Our decoder only needs to know the 1st header value, do not need a multimap
                    headers[entry.Key] = entry.Value.ElementAt(0);
                };
                Decoder decoder = new Decoder(headers, buffer);
                // Supply the logger
                decoder.Logger = decoderLogger;
                string result = decoder.ToJson(false, true, 2);
                return result;
            }
        }
    }
}
