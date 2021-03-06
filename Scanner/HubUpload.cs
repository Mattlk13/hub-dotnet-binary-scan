﻿/**
 * Black Duck Hub .Net Binary Scanner
 *
 * Copyright (C) 2017 Black Duck Software, Inc.
 * http://www.blackducksoftware.com/
 *
 * Licensed to the Apache Software Foundation (ASF) under one
 * or more contributor license agreements. See the NOTICE file
 * distributed with this work for additional information
 * regarding copyright ownership. The ASF licenses this file
 * to you under the Apache License, Version 2.0 (the
 * "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied. See the License for the
 * specific language governing permissions and limitations
 * under the License.
 * 
 * SPDX-License-Identifier: Apache-2.0
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Text;
using System.Threading.Tasks;

namespace Blackduck.Hub
{
    class AuthResult
    {
        public CookieContainer AuthCookies { get; private set; }

        public string CsrfToken { get; private set; }

        public AuthResult(CookieContainer cookieContainer, string csrfToken)
        {
            this.AuthCookies = cookieContainer;
            this.CsrfToken = csrfToken;
        }

    }

	/// <summary>
	/// Performs upload of generated scan data directly to the hub.
	/// </summary>
	class HubUpload
	{
		private const string UPLOAD_URI = "api/v1/scans/upload";
		private const string AUTH_URI = "j_spring_security_check";
        private const string CSRF_TOKEN_HEADER = "X-CSRF-TOKEN";


        public static void UploadScan(string baseUrl, string username, string password, ScannerJsonBuilder scanResult)
		{

			//Prepare the cookies
			var authResult = Authenticate(baseUrl, username, password);
            CookieContainer authCookies = authResult.AuthCookies;

			string requestUrl = $"{(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/")}{UPLOAD_URI}";

			var clientHandler = new HttpClientHandler() { CookieContainer = authCookies };
			var httpClient = new HttpClient(clientHandler);

			var content = new MultipartFormDataContent();

			using (var ms = new MemoryStream())
			using (var writer = new StreamWriter(ms))
			{
				scanResult.Write(writer);
				var streamContent = new ByteArrayContent(ms.ToArray());
				streamContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");
				content.Add(streamContent, "file", "scan.json");

                content.Headers.Add(CSRF_TOKEN_HEADER, authResult.CsrfToken);
                var response = httpClient.PostAsync(requestUrl, content);
				Task.WaitAll(response);

				if (response.Result.StatusCode != HttpStatusCode.OK && response.Result.StatusCode != HttpStatusCode.Created)
				{
					throw new Exception($"Unable to upload to hub. Status code: {response.Result.StatusCode}. {response.Result.ReasonPhrase}");
				}
			}

		}

		/// <summary>
		/// Authenticates with the hub and returns the cookies to set.
		/// </summary>
		/// <param name="baseUrl"></param>
		/// <param name="username"></param>
		/// <param name="password"></param>
		/// <returns></returns>
		private static AuthResult Authenticate(string baseUrl, string username, string password)
		{
			string requestUrl = $"{(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/")}{AUTH_URI}";

			StringBuilder formData = new StringBuilder();
			formData.Append("j_username=" + HttpUtility.UrlEncode(username));
			formData.Append("&j_password=" + HttpUtility.UrlEncode(password));
			byte[] requestData = Encoding.ASCII.GetBytes(formData.ToString());

			HttpContent content = new ByteArrayContent(requestData);
			content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded");

			var requestTask = new HttpClient().PostAsync(requestUrl, content);
			Task.WaitAll(requestTask);

			var response = requestTask.Result;


			if (response.StatusCode != HttpStatusCode.NoContent && response.StatusCode != HttpStatusCode.OK)
			{
				throw new Exception($"Unable to authenticate into {baseUrl}. {response.ReasonPhrase} ({response.StatusCode})");
			}

			IEnumerable<string> cookies;
			if (!response.Headers.TryGetValues("Set-Cookie", out cookies))
				cookies = Enumerable.Empty<string>();
            string csrfToken = response.Headers.GetValues(CSRF_TOKEN_HEADER).FirstOrDefault();

			//Ensure no trailing slash
			var cookieContainer = new CookieContainer();
			var cookieBaseUri = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl.Substring(0, baseUrl.Length - 1) : baseUrl);


			foreach (string cookie in cookies.Where(c => !string.IsNullOrEmpty(c)))
			{
				if (cookie == null) continue;
				string cleansedCookieValue = cookie.Substring(0, cookie.IndexOf(';'));
				string[] split = cleansedCookieValue.Split(new char[] { '=' }, 2);
				Cookie cleansedCookie = new Cookie(split[0], split[1]);
				cookieContainer.Add(cookieBaseUri, cleansedCookie);
			}
            return new AuthResult(cookieContainer, csrfToken);
		}
	}
}
