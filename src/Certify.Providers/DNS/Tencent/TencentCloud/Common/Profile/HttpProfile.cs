/*
 * Copyright (c) 2018 THL A29 Limited, a Tencent company. All Rights Reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied.  See the License for the
 * specific language governing permissions and limitations
 * under the License.
 */

namespace TencentCloud.Common.Profile
{
    /// <summary>
    /// HTTP profiles.
    /// </summary>
    public class HttpProfile
    {
        /// <summary>
        /// HTTPS protocol.
        /// </summary>
        public static readonly string REQ_HTTPS = "https://";

        /// <summary>
        /// HTTP protocol.
        /// </summary>
        public static readonly string REQ_HTTP = "http://";

        /// <summary>
        /// HTTP method POST.
        /// </summary>
        public static readonly string REQ_POST = "POST";

        /// <summary>
        /// HTTP method GET.
        /// </summary>
        public static readonly string REQ_GET = "GET";

        /// <summary>
        /// Time unit, 60 seconds.
        /// </summary>
        public static readonly int TM_MINUTE = 60;

        public HttpProfile()
        {
            this.ReqMethod = REQ_POST;
            this.Endpoint = null;
            this.Protocol = REQ_HTTPS;
            this.Timeout = TM_MINUTE;
        }

        /// <summary>
        /// HTTP request method.
        /// </summary>
        public string ReqMethod { get; set; }

        /// <summary>
        /// Service endpoint, or domain name.
        /// </summary>
        public string Endpoint { get; set; }

        /// <summary>
        /// HTTP protocol.
        /// </summary>
        public string Protocol { get; set; }

        /// <summary>
        /// HTTP request timeout value, in seconds.
        /// </summary>
        public int Timeout { get; set; }

        /// <summary>
        /// HTTP proxy settings.
        /// </summary>
        public string WebProxy { get; set; }
    }
}