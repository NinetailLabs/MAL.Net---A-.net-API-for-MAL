﻿using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text;
using HtmlAgilityPack;
using MAL.NetLogic.Interfaces;

namespace MAL.NetLogic.Classes
{
    public class UserAuthentication : IUserAuthentication
    {
        #region Variables

        private const string LoginUrl = @"http://myanimelist.net/login.php";
        private readonly string _userAgent;
        private readonly IConsoleWriter _consoleWriter;
        private readonly IAuthFactory _authFactory;
        private readonly IWebHttpWebRequestFactory _webRequestFactory;

        #endregion

        #region Constructor

        public UserAuthentication(IAuthFactory authFactory, IWebHttpWebRequestFactory webHttpWebRequestFactory, IConsoleWriter consoleWriter)
        {
            _userAgent = ConfigurationManager.AppSettings["UserAgent"];
            _authFactory = authFactory;
            _webRequestFactory = webHttpWebRequestFactory;
            _consoleWriter = consoleWriter;
        }

        #endregion

        #region Public Methods

        public ILoginData Login(string username, string password, bool canCache = true)
        {
            var loginData = _authFactory.CreateLingData();
            loginData.Username = username;
            loginData.Password = password;
            loginData.CanCache = canCache;

            var cookieJar = new CookieContainer();

            var loginRequest = _webRequestFactory.Create();
            loginRequest.CreateRequest(LoginUrl);
            loginRequest.CookieContainer = cookieJar;
            loginRequest.UserAgent = _userAgent;
            loginRequest.Method = WebRequestMethods.Http.Post;
            loginRequest.ContentType = "application/x-www-form-urlencoded";

            var csrfToken = GetCsrfToken(cookieJar);
            if (string.IsNullOrEmpty(csrfToken))
            {
                loginData.LoginValid = false;
                return loginData;
            }
            var requestText = $"user_name={username}&password={password}&cookie=1&sublogin=Login&submit=1&csrf_token={csrfToken}";
            var stream = loginRequest.GetRequestStream();
            var requestWriter = new StreamWriter(stream);
            requestWriter.Write(requestText);
            requestWriter.Close();

            var response = GetResponse(loginRequest);

            if (!string.IsNullOrEmpty(response))
            {
                if (response.Contains("Could not find that username") || response.Contains("Password is incorrect"))
                {
                    Console.Write($"{DateTime.Now} - ");
                    _consoleWriter.WriteAsLineEnd("[Auth] Auth failed for username and password pair", ConsoleColor.Red);
                    loginData.LoginValid = false;
                }
                else
                {
                    Console.WriteLine($"{DateTime.Now} - ");
                    _consoleWriter.WriteAsLineEnd("[Auth] Auth succeeded for username and password pair", ConsoleColor.Green);
                    loginData.LoginValid = true;
                    loginData.Cookies = cookieJar;

                }
            }
            else
            {
                Console.WriteLine($"{DateTime.Now} - ");
                _consoleWriter.WriteAsLineEnd("[Auth] No response received from the server", ConsoleColor.Red);
                loginData.LoginValid = false;
            }
            return loginData;

        }

        #endregion

        #region Private Methods

        /// <summary>
        /// To be able to do a login we need both the CSRF token embedded in the webpage as well as the cookies.
        /// Retrieve the required values by visiting the login page
        /// </summary>
        /// <returns>CSRF Token embedded in Login Page</returns>
        private string GetCsrfToken(CookieContainer cookieContainer)
        {
            var doc = new HtmlDocument();
            var loginRequest = _webRequestFactory.Create();
            loginRequest.CreateRequest(LoginUrl);
            loginRequest.CookieContainer = cookieContainer;
            loginRequest.GetResponse();

            var docStream = loginRequest.GetResponseStream();
            doc.Load(docStream);
            var tokenNode = doc.DocumentNode.SelectSingleNode("//meta[@name='csrf_token']");
            if (tokenNode == null)
            {
                var text = doc.DocumentNode.InnerText;
                Console.Write($"{DateTime.Now} - ");
                _consoleWriter.WriteAsLineEnd($"[Auth] Failed to access Login.php. Returned value: {text}", ConsoleColor.Red);
            }
            var csrfToken = tokenNode?.Attributes["content"].Value;

            return csrfToken;
        }

        private string GetResponse(IWebHttpWebRequest request)
        {
            var result = string.Empty;
            try
            {
                //var response = await request.GetResponseAsync();
                request.GetResponse();
                var statusCode = request.StatusCode;
                if (statusCode == HttpStatusCode.OK)
                {
                    using (var stream = request.GetResponseStream())
                    {
                        if (stream != null)
                        {
                            var buffer = new byte[2048];
                            using (var desinationStream = new MemoryStream())
                            {
                                int bytesRead;
                                do
                                {
                                    bytesRead = stream.Read(buffer, 0, 2048);
                                    desinationStream.Write(buffer, 0, bytesRead);
                                } while (bytesRead != 0);

                                result = Encoding.UTF8.GetString(desinationStream.ToArray());

                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"{DateTime.Now} - [Auth] - Got response {statusCode} from server");
                    return "Error";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.Now} - ");
                _consoleWriter.WriteAsLineEnd("[Auth] Error occured while waiting for web response. {ex}", ConsoleColor.Red);
            }
            return result;
        }

        #endregion
    }
}