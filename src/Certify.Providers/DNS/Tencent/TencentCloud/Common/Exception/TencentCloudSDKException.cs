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

using System;

namespace TencentCloud.Common
{
    public class TencentCloudSDKException : Exception
    {
        public TencentCloudSDKException(string message)
            : this(message, "")
        {
        }

        public TencentCloudSDKException(string message, string requestId) :
            base(message)
        {
            this.RequestId = requestId;
        }

        public TencentCloudSDKException(string message, Exception innerException) :
            base(message, innerException)
        {
        }

        public TencentCloudSDKException(string message, string errorCode, string requestId) :
            base(message)
        {
            RequestId = requestId;
            ErrorCode = errorCode;
        }

        /// <summary>
        /// UUID of a request.
        /// </summary>
        public string RequestId { get; private set; }

        public string ErrorCode { get; private set; }

        public override string ToString()
        {
            string msg = "";
            if (!string.IsNullOrEmpty(RequestId))
                msg += $"requestId: {RequestId} ";
            return msg + base.ToString();
        }
    }
}