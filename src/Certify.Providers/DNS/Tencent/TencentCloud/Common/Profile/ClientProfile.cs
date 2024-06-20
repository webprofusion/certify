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
    /// Client profiles.
    /// </summary>
    public class ClientProfile
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="signMethod">Signature process method.</param>
        /// <param name="httpProfile">HttpProfile instance.</param>
        public ClientProfile(string signMethod, HttpProfile httpProfile)
        {
            this.SignMethod = signMethod;
            this.HttpProfile = httpProfile;
            this.Language = Language.DEFAULT;
        }

        public ClientProfile(string signMethod)
            : this(signMethod, new HttpProfile())
        {
        }

        public ClientProfile()
            : this(SIGN_TC3SHA256)
        {
        }

        /// <summary>
        /// HTTP profiles, refer to <seealso cref="HttpProfile"/>
        /// </summary>
        public HttpProfile HttpProfile { get; set; }

        /// <summary>
        /// Signature process method.
        /// </summary>
        public string SignMethod { get; set; }

        /// <summary>
        /// valid choices: zh-CN, en-US
        /// </summary>
        public Language Language { get; set; }

        /// <summary>
        /// Signature process version 1, with HmacSHA1.
        /// </summary>
        public const string SIGN_SHA1 = "HmacSHA1";

        /// <summary>
        /// Signature process version 1, with HmacSHA256.
        /// </summary>
        public static string SIGN_SHA256 = "HmacSHA256";

        /// <summary>
        /// Signature process version 3, with TC3-HMAC-SHA256.
        /// </summary>
        public static string SIGN_TC3SHA256 = "TC3-HMAC-SHA256";
    }
}