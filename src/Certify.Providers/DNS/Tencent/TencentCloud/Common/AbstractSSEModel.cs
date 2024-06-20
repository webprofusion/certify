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

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace TencentCloud.Common
{
    [JsonObject]
    public abstract class AbstractSSEModel : AbstractModel, IEnumerable<AbstractSSEModel.SSE>
    {
        public class SSE
        {
            public string Id;
            public string Event;
            public string Data;
            public int Retry;
        }

        [JsonProperty("RequestId")]
        public string RequestId { get; set; }

        internal HttpResponseMessage Response;

        private class Enumerator : IEnumerator<SSE>
        {
            private readonly StreamReader _reader;

            internal Enumerator(AbstractSSEModel response)
            {
                var stream = response.Response.Content.ReadAsStreamAsync()
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                _reader = new StreamReader(stream);
            }

            public void Dispose()
            {
                _reader.Dispose();
            }

            public bool MoveNext()
            {
                if (_reader.EndOfStream)
                    return false;

                var e = new SSE();
                var sb = new StringBuilder();
                while (true)
                {
                    var line = _reader.ReadLine();
                    if (string.IsNullOrEmpty(line))
                        break;

                    //comment
                    if (line[0] == ':')
                        continue;

                    var colonIdx = line.IndexOf(':');
                    var key = line.Substring(0, colonIdx);
                    switch (key)
                    {
                        case "id":
                            e.Id = line.Substring(colonIdx + 1);
                            break;
                        case "event":
                            e.Event = line.Substring(colonIdx + 1);
                            break;
                        case "data":
                            // The spec allows for multiple data fields per event, concatenated them with "\n".
                            if (sb.Length > 0)
                            {
                                sb.Append('\n');
                            }

                            sb.Append(line.Substring(colonIdx + 1));
                            break;
                        case "retry":
                            int.TryParse(line.Substring(colonIdx + 1), out e.Retry);
                            break;
                    }
                }

                e.Data = sb.ToString();
                Current = e;
                return true;
            }

            public void Reset()
            {
                // reset is not supported
                throw new System.NotImplementedException();
            }

            public SSE Current { get; private set; }

            object IEnumerator.Current => Current;
        }

        public IEnumerator<SSE> GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override void ToMap(Dictionary<string, string> map, string prefix)
        {
        }

        public override bool IsStream => Response != null;
    }
}