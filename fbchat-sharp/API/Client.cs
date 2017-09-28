﻿using AngleSharp.Parser.Html;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace fbchat_sharp.API
{
    public enum LoginStatus
    {
        LOGGING_OUT,
        LOGGED_OUT,
        LOGOUT_FAILED,
        LOGGING_IN,
        LOGGED_IN,
        LOGIN_FAILED
    }

    public enum UpdateStatus
    {
        NEW_MESSAGE
    }

    public class LoginEventArgs : EventArgs
    {
        private LoginStatus login_status;
        public LoginEventArgs(LoginStatus _login_status)
        {
            this.login_status = _login_status;
        }

        public LoginStatus Data { get { return login_status; } }
    }

    public class UpdateEventArgs : EventArgs
    {
        private UpdateStatus update_event;
        private object update;

        public UpdateEventArgs(UpdateStatus _update_event, object _data)
        {
            this.update_event = _update_event;
            this.update = _data;
        }

        public dynamic Data { get { return new { Type = update_event, Update = update }; } }
    }

    public class Client
    {
        /*
         * A client for the Facebook Chat (Messenger).
         * See https://fbchat.readthedocs.io for complete documentation of the API.
        */

        private bool listening = false;
        // Whether the client is listening. Used when creating an external event loop to determine when to stop listening/*

        /*
         * The ID of the client.
         * Can be used as `thread_id`. See :ref:`intro_threads` for more info.
        * Note: Modifying this results in undefined behaviour
        */
        private string uid = null;

        // private variables
        private Dictionary<string, string> _header;
        private Dictionary<string, string> payloadDefault;
        private Dictionary<string, string> form;

        private CookieContainer _session
        {
            get { return HttpClientHandler?.CookieContainer; }
        }

        private HttpClientHandler HttpClientHandler;
        private HttpClient http_client;

        private long prev;
        private long tmp_prev;
        private long last_sync;

        private string email;
        private string password;
        private string seq;
        private int req_counter;
        private string client_id;
        private long start_time;
        private string user_channel;
        private string ttstamp;
        private string fb_dtsg;
        private string fb_h;

        private string default_thread_id;
        private ThreadType? default_thread_type;
        private string sticky;
        private string pool;
        private string client;

        public Client(string user_agent = null)
        {
            /*
             * Initializes and logs in the client
             * :param email: Facebook `email`, `id` or `phone number`
             * :param password: Facebook account password
             * : param user_agent: Custom user agent to use when sending requests. If `null`, user agent will be chosen from a premade list(see: any:`utils.USER_AGENTS`)
             * :param max_tries: Maximum number of times to try logging in
             * :param session_cookies: Cookies from a previous session(Will default to login if these are invalid)
             * :type session_cookies: dict
             * :raises: Exception on failed login
            */

            this.HttpClientHandler = new HttpClientHandler() { UseCookies = true, CookieContainer = new CookieContainer(), AllowAutoRedirect = true };
            this.http_client = new HttpClient(this.HttpClientHandler);

            this.sticky = null;
            this.pool = null;
            this.client = "mercury";
            this.default_thread_id = null;
            this.default_thread_type = null;
            this.req_counter = 1;
            this.seq = "0";
            this.payloadDefault = new Dictionary<string, string>();

            if (user_agent == null)
                user_agent = Utils.USER_AGENTS[0];

            this._header = new Dictionary<string, string>() {
                { "Content-Type", "application/x-www-form-urlencoded" },
                { "Referer", ReqUrl.BASE },
                { "Origin", ReqUrl.BASE },
                { "User-Agent", user_agent },
                // { "Connection", "keep-alive" },
            };
        }

        public string GetUserUid()
        {
            return this.uid;
        }

        public async Task tryLogin(IEnumerable<Cookie> session_cookies = null)
        {
            // If session cookies aren"t set, not properly loaded or gives us an invalid session, then do the login
            if (session_cookies == null || !await this.setSession(session_cookies) /*|| !await this.isLoggedIn()*/)
            {
                OnLoginEvent(new LoginEventArgs(LoginStatus.LOGIN_FAILED));
            }
            else
            {
                OnLoginEvent(new LoginEventArgs(LoginStatus.LOGGED_IN));
            }
        }

        public async Task doLogin(string email, string password, int max_tries = 5)
        {
            // If session cookies aren"t set, not properly loaded or gives us an invalid session, then do the login
            await this.login(email, password, max_tries);
        }

        /*
         * INTERNAL REQUEST METHODS
         */

        private Dictionary<string, string> _generatePayload(Dictionary<string, string> query = null)
        {
            /* Adds the following defaults to the payload:
             * __rev, __user, __a, ttstamp, fb_dtsg, __req
             */
            var payload = new Dictionary<string, string>(this.payloadDefault);
            if (query != null)
            {
                foreach (var entry in query)
                {
                    payload[entry.Key] = entry.Value;
                }
            }
            payload["__req"] = Utils.str_base(this.req_counter, 36);
            payload["seq"] = this.seq;
            this.req_counter += 1;
            return payload;
        }

        private async Task<HttpResponseMessage> _get(string url, Dictionary<string, string> query = null, int timeout = 30)
        {
            var payload = this._generatePayload(query);
            var content = new FormUrlEncodedContent(payload);
            var query_string = await content.ReadAsStringAsync();
            var builder = new UriBuilder(url) { Query = query_string };
            var request = new HttpRequestMessage(HttpMethod.Get, builder.ToString());
            foreach (var header in this._header) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            // this.client.Timeout = TimeSpan.FromSeconds(timeout);
            return await http_client.SendAsync(request);
            // return this._session.get(url, headers: this._header, param: payload, timeout: timeout);
        }

        private async Task<HttpResponseMessage> _post(string url, Dictionary<string, string> query = null, int timeout = 30)
        {
            var payload = this._generatePayload(query);
            var content = new FormUrlEncodedContent(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            foreach (var header in this._header) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            request.Content = content;
            // this.client.Timeout = TimeSpan.FromSeconds(timeout);
            return await http_client.SendAsync(request);
            // return this._session.post(url, headers: this._header, data: payload, timeout: timeout);
        }

        private async Task<HttpResponseMessage> _cleanGet(string url, int timeout = 30)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var header in this._header) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            // this.client.Timeout = TimeSpan.FromSeconds(timeout);
            return await http_client.SendAsync(request);
            // return this._session.get(url, headers: this._header, param: query, timeout: timeout);
        }

        private async Task<HttpResponseMessage> _cleanPost(string url, Dictionary<string, string> query = null, int timeout = 30)
        {
            this.req_counter += 1;
            var content = new FormUrlEncodedContent(query);
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            foreach (var header in this._header) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            request.Content = content;
            // this.client.Timeout = TimeSpan.FromSeconds(timeout);
            return await http_client.SendAsync(request);
            // return this._session.post(url, headers: this._header, data: query, timeout: timeout);
        }

        private async Task<HttpResponseMessage> _postFile(string url, object files = null, Dictionary<string, string> query = null, int timeout = 30)
        {
            throw new NotImplementedException();
            // var payload = this._generatePayload(query);
            // Removes "Content-Type" from the header
            // var headers = new Dictionary<string, string>((i, this._header[i]) for i in this._header if i != "Content-Type") ;
            // var response = await client.PostAsync(uri, content);
            // return this._session.post(url, headers: headers, data: payload, timeout: timeout, files: files);
        }

        private async Task<List<JToken>> graphql_requests(List<GraphQL> queries)
        {
            /*
             * :raises: Exception if request failed
             */
            var payload = new Dictionary<string, string>(){
                { "method", "GET"},
                { "response_format", "json"},
                { "queries", ConcatJSONDecoder.graphql_queries_to_json(queries)}
            };

            var j = ConcatJSONDecoder.graphql_response_to_json((string)await Utils.checkRequest(await this._post(ReqUrl.GRAPHQL, payload), do_json_check: false));

            return j;
        }

        private async Task<JToken> graphql_request(GraphQL query)
        {
            /*
             * Shorthand for `graphql_requests(query)[0]`
             * :raises: Exception if request failed
             */
            return (await this.graphql_requests(new[] { query }.ToList()))[0];
        }

        /*
         * END INTERNAL REQUEST METHODS
         */

        /*
         * LOGIN METHODS
         */

        private void _resetValues()
        {
            this.payloadDefault = new Dictionary<string, string>();
            this.HttpClientHandler = new HttpClientHandler()
            {
                UseCookies = true,
                CookieContainer = new CookieContainer(),
                AllowAutoRedirect = true
            };
            this.http_client = new HttpClient(HttpClientHandler);
            this.req_counter = 1;
            this.seq = "0";
            this.uid = null;
        }

        private async Task _postLogin()
        {
            this.payloadDefault = new Dictionary<string, string>();
            this.client_id = ((int)(new Random().NextDouble() * 2147483648)).ToString("X4").Substring(2);
            this.start_time = Utils.now();
            var cookies = (this._session.GetCookies(new Uri(ReqUrl.BASE)).Cast<Cookie>()
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(c => c.Key, c => c.First().Value, StringComparer.OrdinalIgnoreCase));
            this.uid = cookies["c_user"];
            this.user_channel = "p_" + this.uid;
            this.ttstamp = "";

            var r = await this._get(ReqUrl.BASE);
            string soup = (string)await Utils.checkRequest(r, false);
            var parser = new HtmlParser();
            var document = parser.Parse(soup);
            this.fb_dtsg = document.QuerySelectorAll("input").Where(i => i.GetAttribute("name").Equals("fb_dtsg")).Select(i => i.GetAttribute("value")).First();
            this.fb_h = document.QuerySelectorAll("input").Where(i => i.GetAttribute("name").Equals("h")).Select(i => i.GetAttribute("value")).First();
            foreach (var i in this.fb_dtsg)
            {
                this.ttstamp += ((int)i).ToString();
            }
            this.ttstamp += "2";
            // Set default payload
            this.payloadDefault["__rev"] = soup.Split(new[] { "\"client_revision\":" }, StringSplitOptions.RemoveEmptyEntries)[1].Split(',')[0];
            this.payloadDefault["__user"] = this.uid;
            this.payloadDefault["__a"] = "1";
            this.payloadDefault["ttstamp"] = this.ttstamp;
            this.payloadDefault["fb_dtsg"] = this.fb_dtsg;

            this.form = new Dictionary<string, string>() {
                { "channel", this.user_channel },
                { "partition", "-2" },
                { "clientid", this.client_id },
                { "viewer_uid", this.uid },
                { "uid", this.uid },
                { "state", "active" },
                { "format", "json" },
                { "idle", "\0" },
                { "cap", "8" }
            };

            this.prev = Utils.now();
            this.tmp_prev = Utils.now();
            this.last_sync = Utils.now();
        }

        private async Task<Tuple<bool, string>> _login()
        {
            if (string.IsNullOrEmpty(this.email) || string.IsNullOrEmpty(this.password))
            {
                throw new Exception("Email and password not found.");
            }

            var r = await this._get(ReqUrl.MOBILE);
            string soup = (string)await Utils.checkRequest(r, false);
            var parser = new HtmlParser();
            var document = parser.Parse(soup);
            var data = document.QuerySelectorAll("input").Where(i => i.HasAttribute("name") && i.HasAttribute("value")).Select(i => new { Key = i.GetAttribute("name"), Value = i.GetAttribute("value") })
                .GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(c => c.Key, c => c.First().Value, StringComparer.OrdinalIgnoreCase);

            data["email"] = this.email;
            data["pass"] = this.password;
            data["login"] = "Log In";

            r = await this._cleanPost(ReqUrl.LOGIN, data);
            soup = (string)await Utils.checkRequest(r, false);

            if (r.RequestMessage.RequestUri.ToString().Contains("checkpoint") &&
                (soup.Contains("Enter Security Code to Continue") || soup.Contains("Enter Login Code to Continue")))
            {
                r = await this._2FA(r);
            }

            // Sometimes Facebook tries to show the user a "Save Device" dialog
            if (r.RequestMessage.RequestUri.ToString().Contains("save-device"))
            {
                r = await this._cleanGet(ReqUrl.SAVE_DEVICE);
            }

            if (r.RequestMessage.RequestUri.ToString().Contains("home"))
            {
                await this._postLogin();
                return new Tuple<bool, string>(true, r.RequestMessage.RequestUri.ToString());
            }
            else
            {
                return new Tuple<bool, string>(false, r.RequestMessage.RequestUri.ToString());
            }
        }

        private async Task<HttpResponseMessage> _2FA(HttpResponseMessage r)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> isLoggedIn()
        {
            /*
             * Sends a request to Facebook to check the login status
             * :return: true if the client is still logged in
             * :rtype: bool
             */
            // Send a request to the login url, to see if we"re directed to the home page
            var r = await this._cleanGet(ReqUrl.LOGIN);
            return (r.RequestMessage.RequestUri.ToString().Contains("home"));
        }

        public IEnumerable<Cookie> getSession(string url = null)
        {
            /*
             * Retrieves session cookies
             * :return: A list containing session cookies
             * : rtype: IEnumerable
             */
            return this._session.GetCookies(new Uri(url != null ? url : ReqUrl.BASE)).Cast<Cookie>();
        }

        public async Task<bool> setSession(IEnumerable<Cookie> session_cookies = null)
        {
            /*
             * Loads session cookies
             * :param session_cookies: A dictionay containing session cookies
             * : type session_cookies: dict
             * : return: false if `session_cookies` does not contain proper cookies
             * : rtype: bool
             */

            // Quick check to see if session_cookies is formatted properly
            if (session_cookies == null ||
                !(session_cookies.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(c => c.Key, c => c.First().Value, StringComparer.OrdinalIgnoreCase)).ContainsKey("c_user"))
            {
                return false;
            }

            try
            {
                // Load cookies into current session
                foreach (string url in new[] { ReqUrl.LISTEN, ReqUrl.BASE })
                {
                    var current_cookies = this._session.GetCookies(new Uri(url)).Cast<Cookie>();

                    foreach (var cookie in session_cookies)
                    {
                        if (!current_cookies.Any(c => c.Name.Equals(cookie.Name)))
                            this._session.Add(new Uri(url), new Cookie(cookie.Name, cookie.Value));
                    }
                }
                await this._postLogin();
            }
            catch (Exception)
            {
                this._resetValues();
                return false;
            }

            return true;
        }

        private async Task login(string email, string password, int max_tries = 5)
        {
            /*
             * Uses `email` and `password` to login the user (If the user is already logged in, this will do a re-login)
             * :param email: Facebook `email` or `id` or `phone number`
             * :param password: Facebook account password
             * : param max_tries: Maximum number of times to try logging in
             * :type max_tries: int
             * :raises: Exception on failed login
             */
            OnLoginEvent(new LoginEventArgs(LoginStatus.LOGGING_IN));

            if (max_tries < 1)
            {
                throw new Exception("Cannot login: max_tries should be at least one");
            }

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                throw new Exception("Email and password not set");
            }

            this.email = email;
            this.password = password;

            // Holds result of last login
            Tuple<bool, string> tuple_login = null;

            foreach (int i in Enumerable.Range(1, max_tries + 1))
            {
                tuple_login = await this._login();
                if (!tuple_login.Item1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    this._resetValues();
                    continue;
                }
                else
                {
                    OnLoginEvent(new LoginEventArgs(LoginStatus.LOGGED_IN));
                    return;
                }
            }

            throw new Exception(string.Format("Login failed. Check email/password. (Failed on url: {0})", tuple_login.Item2));
        }

        public async Task doLogout()
        {
            OnLoginEvent(new LoginEventArgs(LoginStatus.LOGGING_OUT));

            /*
             * Safely logs out the client
             * :param timeout: See `requests timeout < http://docs.python-requests.org/en/master/user/advanced/#timeouts>`_
             * :return: true if the action was successful
             * : rtype: bool
             */
            var data = new Dictionary<string, string>() {
                { "ref", "mb"},
                { "h", this.fb_h }
            };

            var r = await this._get(ReqUrl.LOGOUT, data);

            this._resetValues();

            if (r.IsSuccessStatusCode)
            {
                OnLoginEvent(new LoginEventArgs(LoginStatus.LOGGED_OUT));
            }
            else
            {
                throw new Exception("Logout failed");
            }
        }

        /*
         * END LOGIN METHODS
         */

        /*
         * DEFAULT THREAD METHODS
         */

        private Tuple<string, ThreadType?> _getThread(string given_thread_id = null, ThreadType given_thread_type = ThreadType.USER)
        {
            /*
             * Checks if thread ID is given, checks if default is set and returns correct values
             * :raises ValueError: If thread ID is not given and there is no default
             * :return: Thread ID and thread type
             * : rtype: tuple
             */

            if (given_thread_id == null)
            {
                if (this.default_thread_id != null)
                {
                    return new Tuple<string, ThreadType?>(this.default_thread_id, this.default_thread_type);
                }
                else
                {
                    throw new ArgumentException("Thread ID is not set");
                }
            }
            else
            {
                return new Tuple<string, ThreadType?>(given_thread_id, given_thread_type);
            }
        }

        private void setDefaultThread(string thread_id, ThreadType? thread_type)
        {
            /*
             * Sets default thread to send messages to
             * :param thread_id: User / FGroup ID to default to.See :ref:`intro_threads`
             * :param thread_type: See:ref:`intro_threads`
             * :type thread_type: models.ThreadType
            */
            this.default_thread_id = thread_id;
            this.default_thread_type = thread_type;
        }

        private void resetDefaultThread()
        {
            /*Resets default thread*/
            this.setDefaultThread(null, null);
        }

        /*
        END DEFAULT THREAD METHODS
        */

        /*
         * FETCH METHODS
         */

        public async Task<List<User>> fetchAllUsers()
        {
            /*
             * Gets all users the client is currently chatting with
             * :return: :class:`models.User` objects
             * :rtype: list
             * :raises: Exception if request failed
             */

            var data = new Dictionary<string, string>() {
                { "viewer", this.uid },
            };
            var j = (JToken)await Utils.checkRequest(await this._post(ReqUrl.ALL_USERS, query: data));
            if (j["payload"] == null)
            {
                throw new Exception("Missing payload");
            }

            var users = new List<User>();

            foreach (var k in j["payload"].Value<JObject>().Properties())
            {
                if (new[] { "user", "friend" }.Contains(k.Value["type"].Value<string>()))
                {
                    if (new[] { "0", "\0" }.Contains(k["id"].Value<string>()))
                    {
                        // Skip invalid users
                        continue;
                    }
                    users.Add(new User(k["id"].Value<string>(), first_name: k["firstName"].Value<string>(), url: k["uri"].Value<string>(), photo: k["thumbSrc"].Value<string>(), name: k["name"].Value<string>(), is_friend: k["is_friend"].Value<bool>(), gender: GENDER.standard_GENDERS[k["gender"].Value<int>()]));
                }
            }

            return users;
        }

        public async Task<List<User>> searchForUsers(string name, int limit = 1)
        {
            /*
             * Find and get user by his/ her name
             : param name: Name of the user
             :param limit: The max. amount of users to fetch
             : return: :class:`models.User` objects, ordered by relevance
             :rtype: list
             :raises: Exception if request failed
             */

            var j = await this.graphql_request(new GraphQL(query: GraphQL.SEARCH_USER, param: new Dictionary<string, string>() {
                { "search", name }, { "limit", limit.ToString() }
             }));

            return j[name]["users"]["nodes"].Select(node => ConcatJSONDecoder.graphql_to_user(node)).ToList();
        }

        public async Task<List<FPage>> searchForPages(string name, int limit = 1)
        {
            /*
             * Find and get page by its name
             * : param name: Name of the page
             * :return: :class:`models.Page` objects, ordered by relevance
             * :rtype: list
             * :raises: Exception if request failed
             */

            var j = await this.graphql_request(new GraphQL(query: GraphQL.SEARCH_PAGE, param: new Dictionary<string, string>() {
                { "search", name }, { "limit", limit.ToString() }
            }));

            return j[name]["pages"]["nodes"].Select(node => ConcatJSONDecoder.graphql_to_page(node)).ToList();
        }

        public async Task<List<FGroup>> searchForGroups(string name, int limit = 1)
        {
            /*
             * Find and get group thread by its name
             * :param name: Name of the group thread
             * : param limit: The max. amount of groups to fetch
             * : return: :class:`models.FGroup` objects, ordered by relevance
             * :rtype: list
             * :raises: Exception if request failed
             * */

            var j = await this.graphql_request(new GraphQL(query: GraphQL.SEARCH_GROUP, param: new Dictionary<string, string>() {
              { "search", name }, {"limit", limit.ToString() }
            }));

            return j["viewer"]["groups"]["nodes"].Select(node => ConcatJSONDecoder.graphql_to_group(node)).ToList();
        }

        public async Task<List<Thread>> searchForThreads(string name, int limit = 1)
        {
            /*
             * Find and get a thread by its name
             * :param name: Name of the thread
             * :param limit: The max. amount of groups to fetch
             * : return: :class:`models.User`, :class:`models.FGroup` and :class:`models.Page` objects, ordered by relevance
             * :rtype: list
             * :raises: Exception if request failed
             */

            var j = await this.graphql_request(new GraphQL(query: GraphQL.SEARCH_THREAD, param: new Dictionary<string, string>(){
                { "search", name }, {"limit", limit.ToString() }
            }));

            List<Thread> rtn = new List<Thread>();

            foreach (var node in j[name]["threads"]["nodes"])
            {
                if (node["__typename"].Value<string>().Equals("User"))
                {
                    rtn.Add(ConcatJSONDecoder.graphql_to_user(node));
                }
                else if (node["__typename"].Value<string>().Equals("MessageThread"))
                {
                    // MessageThread => FGroup thread
                    rtn.Add(ConcatJSONDecoder.graphql_to_group(node));
                }
                else if (node["__typename"].Value<string>().Equals("Page"))
                {
                    rtn.Add(ConcatJSONDecoder.graphql_to_page(node));
                }
                else if (node["__typename"].Value<string>().Equals("FGroup"))
                {
                    // We don"t handle Facebook "FGroups"
                    continue;
                }
                else
                {
                    Debug.WriteLine(string.Format("Unknown __typename: {0} in {1}", node["__typename"].Value<string>(), node));
                }
            }

            return rtn;
        }

        private async Task<Dictionary<string, Dictionary<string, object>>> _fetchInfo(string[] ids)
        {
            var data = new Dictionary<string, string>();
            foreach (var obj in ids.Select((x, index) => new { _id = x, i = index }))
                data.Add(string.Format("ids[{0}]", obj.i), obj._id);

            var j = (JToken)await Utils.checkRequest(await this._post(ReqUrl.INFO, data));

            if (j["payload"]["profiles"] == null)
            {
                throw new Exception("No users/pages returned");
            }

            var entries = new Dictionary<string, Dictionary<string, object>>();

            foreach (var k in j["payload"]["profiles"].Value<JObject>().Properties())
            {
                if (new[] { "user", "friend" }.Contains(k.Value["type"].Value<string>()))
                {
                    entries[k.Name] = new Dictionary<string, object>() {
                        { "id", k.Name },
                        {"type", ThreadType.USER.ToString() },
                        {"url", k["uri"].Value<string>() },
                        {"first_name", k["firstName"].Value<string>() },
                        {"is_viewer_friend", k["is_friend"].Value<bool>() },
                        {"gender", k["gender"].Value<string>() },
                        {"profile_picture", new Dictionary<string,string>() { { "uri", k["thumbSrc"].Value<string>() } } },
                        { "name", k["name"].Value<string>() }
                    };
                }
                else if (k.Value["type"].Value<string>().Equals("page"))
                {
                    entries[k.Name] = new Dictionary<string, object>() {
                        { "id", k.Name},
                        { "type", ThreadType.PAGE.ToString()},
                        {"url", k["uri"].Value<string>()},
                        {"profile_picture", new Dictionary<string,string>(){ { "uri", k["thumbSrc"].Value<string>() } } },
                        { "name", k["name"].Value<string>() }
                    };
                }
                else
                {
                    throw new Exception(string.Format("{0} had an unknown thread type: {1}", k.Name, k.Value));
                }
            }

            return entries;
        }

        public async Task<Dictionary<string, User>> fetchUserInfo(string[] user_ids)
        {
            /*
             * Get users" info from IDs, unordered
             * ..warning::
             * Sends two requests, to fetch all available info!
             * :param user_ids: One or more user ID(s) to query
             * :return: :class:`models.User` objects, labeled by their ID
             * :rtype: dict
             * :raises: Exception if request failed
             */

            var threads = await this.fetchThreadInfo(user_ids);
            var users = new Dictionary<string, User>();

            foreach (var k in threads.Keys)
            {
                if (threads[k].type == ThreadType.USER)
                {
                    users[k] = (User)threads[k];
                }
                else
                {
                    throw new Exception(string.Format("Thread {0} was not a user", threads[k]));
                }
            }

            return users;
        }

        public async Task<Dictionary<string, FPage>> fetchPageInfo(string[] page_ids)
        {
            /*
             * Get pages" info from IDs, unordered
             * ..warning::
             * Sends two requests, to fetch all available info!
             * :param page_ids: One or more page ID(s) to query
             * :return: :class:`models.Page` objects, labeled by their ID
             * :rtype: dict
             * :raises: Exception if request failed
             */

            var threads = await this.fetchThreadInfo(page_ids);
            var pages = new Dictionary<string, FPage>();

            foreach (var k in threads.Keys)
            {
                if (threads[k].type == ThreadType.PAGE)
                {
                    pages[k] = (FPage)threads[k];
                }
                else
                {
                    throw new Exception(string.Format("Thread {0} was not a page", threads[k]));
                }
            }

            return pages;
        }

        public async Task<Dictionary<string, FGroup>> fetchFGroupInfo(string[] group_ids)
        {
            /*
             * Get groups" info from IDs, unordered
             * :param group_ids: One or more group ID(s) to query
             * :return: :class:`models.FGroup` objects, labeled by their ID
             * :rtype: dict
             * :raises: Exception if request failed
             */

            var threads = await this.fetchThreadInfo(group_ids);
            var groups = new Dictionary<string, FGroup>();

            foreach (var k in threads.Keys)
            {
                if (threads[k].type == ThreadType.GROUP)
                {
                    groups[k] = (FGroup)threads[k];
                }
                else
                {
                    throw new Exception(string.Format("Thread {0} was not a group", threads[k]));
                }
            }

            return groups;
        }

        public async Task<Dictionary<string, Thread>> fetchThreadInfo(string[] thread_ids)
        {
            /*
             * Get threads" info from IDs, unordered
             * ..warning::
             * Sends two requests if users or pages are present, to fetch all available info!
             * :param thread_ids: One or more thread ID(s) to query
             * :return: :class:`models.Thread` objects, labeled by their ID
             * :rtype: dict
             * :raises: Exception if request failed
             */

            var queries = new List<GraphQL>();
            foreach (var thread_id in thread_ids)
            {
                queries.Add(new GraphQL(doc_id: "1386147188135407", param: new Dictionary<string, string>() {
                    { "id", thread_id },
                    {"message_limit", 0.ToString() },
                    {"load_messages", false.ToString() },
                    {"load_read_receipts", false.ToString() }
                }));
            }

            var j = await this.graphql_requests(queries);

            foreach (var obj in j.Select((x, index) => new { entry = x, i = index }))
            {
                if (obj.entry["message_thread"] == null)
                {
                    // If you don"t have an existing thread with this person, attempt to retrieve user data anyways
                    j[obj.i]["message_thread"] = new JObject(
                                                    new JProperty("thread_key",
                                                        new JObject(
                                                            new JProperty("other_user_id", thread_ids[obj.i]))),
                                                    new JProperty("thread_type", "ONE_TO_ONE"));
                }
            }

            var pages_and_user_ids = j.Where(k => k["message_thread"]["thread_type"].Value<string>().Equals("ONE_TO_ONE"))
                .Select(k => k["message_thread"]["thread_key"]["other_user_id"].Value<string>());
            var pages_and_users = new Dictionary<string, Dictionary<string, object>>();
            if (pages_and_user_ids.Count() != 0)
            {
                pages_and_users = await this._fetchInfo(pages_and_user_ids.ToArray());
            }

            var rtn = new Dictionary<string, Thread>();
            foreach (var obj in j.Select((x, index) => new { entry = x, i = index }))
            {
                var entry = obj.entry["message_thread"];
                if (entry["thread_type"].Value<string>().Equals("GROUP"))
                {
                    var _id = entry["thread_key"]["thread_fbid"].Value<string>();
                    rtn[_id] = ConcatJSONDecoder.graphql_to_group(entry);
                }
                else if (entry["thread_type"].Value<string>().Equals("ONE_TO_ONE"))
                {
                    var _id = entry["thread_key"]["other_user_id"].Value<string>();
                    if (pages_and_users["_id"] == null)
                    {
                        throw new Exception(string.Format("Could not fetch thread {0}", _id));
                    }
                    foreach (var elem in pages_and_users[_id])
                    {
                        // entry[elem.Key] = elem.Value;
                    }
                    if (entry["type"].Value<int>() == (int)ThreadType.USER)
                    {
                        rtn[_id] = ConcatJSONDecoder.graphql_to_user(entry);
                    }
                    else
                    {
                        rtn[_id] = ConcatJSONDecoder.graphql_to_page(entry);
                    }
                }
                else
                {
                    throw new Exception(string.Format("{0} had an unknown thread type: {1}", thread_ids[obj.i], entry));
                }
            }

            return rtn;
        }

        public async Task<List<Message>> fetchThreadMessages(string thread_id = null, int limit = 20, string before = null)
        {
            /*
             * Get the last messages in a thread
             * :param thread_id: User / FGroup ID to default to.See :ref:`intro_threads`
             * :param limit: Max.number of messages to retrieve
             * : param before: A timestamp, indicating from which point to retrieve messages
             * :type limit: int
             * :type before: int
             * :return: :class:`models.Message` objects
             * :rtype: list
             * :raises: Exception if request failed
             */

            var dict = new Dictionary<string, string>() {
                { "id", thread_id},
                { "message_limit", limit.ToString()},
                { "load_messages", true.ToString()},
                { "load_read_receipts", false.ToString()},
            };
            if (before != null)
                dict.Add("before", before);

            var j = await this.graphql_request(new GraphQL(doc_id: "1386147188135407", param: dict));

            if (j["message_thread"] == null || j["message_thread"].Type == JTokenType.Null)
            {
                throw new Exception(string.Format("Could not fetch thread {0}", thread_id));
            }

            return j["message_thread"]["messages"]["nodes"].Select(message => ConcatJSONDecoder.graphql_to_message(message)).Reverse().ToList();
        }

        public async Task<List<Thread>> fetchThreadList(int offset = 0, int limit = 20)
        {
            /*
             * Get thread list of your facebook account
             * :param offset: The offset, from where in the list to recieve threads from
             * :param limit: Max.number of threads to retrieve.Capped at 20
             * :type offset: int
             * :type limit: int
             * :return: :class:`models.Thread` objects
             * :rtype: list
             * :raises: Exception if request failed
             */

            if (limit > 20 || limit < 1)
            {
                throw new Exception("`limit` should be between 1 and 20");
            }

            var data = new Dictionary<string, string>() {
                { "client", this.client},
                { "inbox[offset]", offset.ToString()},
                { "inbox[limit]", limit.ToString()},
            };

            var j = (JToken)await Utils.checkRequest(await this._post(ReqUrl.THREADS, data));
            if (j["payload"] == null)
            {
                throw new Exception(string.Format("Missing payload: {0}, with data: {1}", j, data));
            }

            var participants = new Dictionary<string, Thread>();
            foreach (var p in j["payload"]["participants"])
            {
                if (p["type"].Value<string>() == "page")
                {
                    participants[p["fbid"].Value<string>()] = new FPage(p["fbid"].Value<string>(), url: p["href"].Value<string>(), photo: p["image_src"].Value<string>(), name: p["name"].Value<string>());
                }
                else if (p["type"].Value<string>() == "user")
                {
                    participants[p["fbid"].Value<string>()] = new User(p["fbid"].Value<string>(), url: p["href"].Value<string>(), first_name: p["short_name"].Value<string>(), is_friend: p["is_friend"].Value<bool>(), gender: GENDER.standard_GENDERS[p["gender"].Value<int>()], photo: p["image_src"].Value<string>(), name: p["name"].Value<string>());
                }
                else
                {
                    throw new Exception(string.Format("A participant had an unknown type {0}: {1}", p["type"].Value<string>(), p));
                }
            }

            var entries = new List<Thread>();
            foreach (var k in j["payload"]["threads"])
            {
                if (k["thread_type"].Value<int>() == 1)
                {
                    if (!participants.ContainsKey(k["other_user_fbid"].Value<string>()))
                    {
                        throw new Exception(string.Format("A thread was not in participants: {0}", j["payload"]));
                    }
                    participants[k["other_user_fbid"].Value<string>()].message_count = k["message_count"].Value<int>();
                    entries.Add(participants[k["other_user_fbid"].Value<string>()]);
                }
                else if (k["thread_type"].Value<int>() == 2)
                {
                    var part = new HashSet<string>(k["participants"].Select(p => p.Value<string>().Replace("fbid:", "")));
                    entries.Add(new FGroup(k["thread_fbid"].Value<string>(), participants: part, photo: k["image_src"].Value<string>(), name: k["name"].Value<string>(), message_count: k["message_count"].Value<int>()));
                }
                else
                {
                    throw new Exception(string.Format("A thread had an unknown thread type: {0}", k));
                }
            }

            return entries;
        }

        public async Task<Dictionary<string, object>> fetchUnread()
        {
            /*
             * ..todo::
             * Documenting this
             * :raises: Exception if request failed
             */

            var form = new Dictionary<string, string>() {
                { "client", "mercury_sync"},
                { "folders[0]", "inbox"},
                { "last_action_timestamp", (Utils.now() - 60 * 1000).ToString()}
                // "last_action_timestamp": 0
            };

            var j = (JToken)await Utils.checkRequest(await this._post(ReqUrl.THREAD_SYNC, form));

            return new Dictionary<string, object>() {
                { "message_counts", j["payload"]["message_counts"].Value<int>() },
                { "unseen_threads", j["payload"]["unseen_thread_ids"] }
            };
        }

        /*
        END FETCH METHODS
        */

        /*
         * SEND METHODS
         */

        private Dictionary<string, string> _getSendData(string thread_id = null, ThreadType thread_type = ThreadType.USER)
        {
            /*Returns the data needed to send a request to `SendURL`*/
            string messageAndOTID = Utils.generateOfflineThreadingID();
            long timestamp = Utils.now();
            var date = DateTime.Now;
            var data = new Dictionary<string, string> {
                { "client", this.client },
                { "author" , "fbid:" + this.uid },
                { "timestamp" , timestamp.ToString() },
                { "timestamp_absolute" , "Today" },
                { "timestamp_relative" , date.Hour + ":" + date.Minute.ToString().PadLeft(2,'0') },
                { "timestamp_time_passed" , "0" },
                { "is_unread" , false.ToString() },
                { "is_cleared" , false.ToString() },
                { "is_forward" , false.ToString() },
                { "is_filtered_content" , false.ToString() },
                { "is_filtered_content_bh", false.ToString() },
                { "is_filtered_content_account", false.ToString() },
                { "is_filtered_content_quasar", false.ToString() },
                { "is_filtered_content_invalid_app", false.ToString() },
                { "is_spoof_warning" , false.ToString() },
                { "source" , "source:chat:web" },
                { "source_tags[0]" , "source:chat" },
                { "html_body" , false.ToString() },
                { "ui_push_phase" , "V3" },
                { "status" , "0" },
                { "offline_threading_id", messageAndOTID },
                { "message_id" , messageAndOTID },
                { "threading_id", Utils.generateMessageID(this.client_id) },
                { "ephemeral_ttl_mode:", "0" },
                { "manual_retry_cnt" , "0" },
                { "signatureID" , Utils.getSignatureID() },
            };

            // Set recipient
            if (new[] { ThreadType.USER, ThreadType.PAGE }.Contains(thread_type))
            {
                data["other_user_fbid"] = thread_id;
            }
            else if (thread_type == ThreadType.GROUP)
            {
                data["thread_fbid"] = thread_id;
            }

            return data;
        }

        private async Task<string> _doSendRequest(Dictionary<string, string> data)
        {
            /*Sends the data to `SendURL`, and returns the message ID or null on failure*/
            var j = (JToken)await Utils.checkRequest(await this._post(ReqUrl.SEND, data));
            string message_id = null;

            try
            {
                var message_ids = j["payload"]["actions"].Where(action => action["message_id"] != null).Select(action => action["message_id"].Value<string>()).ToList();
                if (message_ids.Count != 1)
                {
                    Debug.WriteLine(string.Format("Got multiple message ids back: {0}", message_ids));
                }
                message_id = message_ids[0];
            }
            catch
            {
                throw new Exception(string.Format("Error when sending message: No message IDs could be found: {0}", j));
            }

            // update JS token if receive from response
            if (j["jsmods"] != null && j["jsmods"]["require"] != null)
            {
                try
                {
                    this.payloadDefault["fb_dtsg"] = j["jsmods"]["require"][0][3][0].Value<string>();
                }
                catch
                {
                    Debug.WriteLine("Error when update fb_dtsg. Facebook might have changed protocol.");
                }
            }

            return message_id;
        }

        public async Task<string> sendMessage(string message, string thread_id = null, ThreadType thread_type = ThreadType.USER)
        {
            /*
             * Sends a message to a thread
             * :param message: Message to send
             * : param thread_id: User / FGroup ID to send to. See:ref:`intro_threads`
             * :param thread_type: See:ref:`intro_threads`
             * :type thread_type: models.ThreadType
             * :return: :ref:`Message ID < intro_message_ids >` of the sent message
             * :raises: Exception if request failed
             */
            var thread = this._getThread(thread_id, thread_type);
            var data = this._getSendData(thread_id, thread_type);

            data["action_type"] = "ma-type:user-generated-message";
            data["body"] = string.IsNullOrWhiteSpace(message) ? "" : message;
            data["has_attachment"] = false.ToString();
            data["specific_to_list[0]"] = "fbid:" + thread_id;
            data["specific_to_list[1]"] = "fbid:" + this.uid;

            return await this._doSendRequest(data);
        }

        /*
         * END SEND METHODS
         */

        /*
         * LISTEN METHODS
         */

        private async Task _ping(string sticky, string pool)
        {
            var data = new Dictionary<string, string>() {
                { "channel", this.user_channel },
                {"clientid", this.client_id },
                {"partition", (-2).ToString() },
                {"cap", 0.ToString() },
                {"uid", this.uid },
                {"sticky_token", sticky },
                {"sticky_pool", pool },
                {"viewer_uid", this.uid },
                {"state", "active" }
            };
            await Utils.checkRequest(await this._get(ReqUrl.PING, data), do_json_check: false);
        }

        private async Task<Tuple<string, string>> _fetchSticky()
        {
            /*Call pull api to get sticky and pool parameter, newer api needs these parameters to work*/
            var data = new Dictionary<string, string>() {
                { "msgs_recv", 0.ToString()},
                {"channel", this.user_channel},
                {"clientid", this.client_id }
            };

            var j = (JToken)await Utils.checkRequest(await this._get(ReqUrl.STICKY, data));

            if (j["lb_info"] == null)
            {
                throw new Exception("Missing lb_info");
            }

            return new Tuple<string, string>(j["lb_info"]["sticky"].Value<string>(), j["lb_info"]["pool"].Value<string>());
        }

        private async Task<JToken> _pullMessage(string sticky, string pool)
        {
            /*Call pull api with seq value to get message data.*/
            var data = new Dictionary<string, string>() {
                { "msgs_recv", 0.ToString() },
                {"sticky_token", sticky },
                {"sticky_pool", pool },
                {"clientid", this.client_id },
            };

            var j = (JToken)await Utils.checkRequest(await this._get(ReqUrl.STICKY, data));

            this.seq = j["seq"] != null ? j["seq"].Value<string>() : "0";
            return j;
        }

        private Tuple<string, ThreadType> getThreadIdAndThreadType(JToken msg_metadata)
        {
            /*Returns a tuple consisting of thread ID and thread type*/
            string id_thread = null;
            ThreadType type_thread = ThreadType.USER;
            if (msg_metadata["threadKey"]["threadFbId"] != null)
            {
                id_thread = (msg_metadata["threadKey"]["threadFbId"].Value<string>());
                type_thread = ThreadType.GROUP;
            }
            else if (msg_metadata["threadKey"]["otherUserFbId"] != null)
            {
                id_thread = (msg_metadata["threadKey"]["otherUserFbId"].Value<string>());
                type_thread = ThreadType.USER;
            }
            return new Tuple<string, ThreadType>(id_thread, type_thread);
        }

        private void _parseMessage(JToken content)
        {
            /*Get message and author name from content. May contain multiple messages in the content.*/

            if (content["ms"] == null) return;

            foreach (var m in content["ms"])
            {
                var mtype = m["type"].Value<string>();
                try
                {
                    // Things that directly change chat
                    if (mtype == "delta")
                    {
                        var delta = m["delta"];
                        var delta_type = m["type"].Value<string>();
                        var metadata = delta["messageMetadata"];

                        var mid = metadata?["messageId"].Value<string>();
                        var author_id = metadata?["actorFbId"].Value<string>();
                        var ts = metadata?["timestamp"].Value<string>();

                        // Added participants
                        if (delta["addedParticipants"] != null)
                        {
                            // added_ids = [str(x['userFbId']) for x in delta['addedParticipants']];
                            // thread_id = str(metadata['threadKey']['threadFbId']);
                            // this.onPeopleAdded(mid = mid, added_ids = added_ids, author_id = author_id, thread_id = thread_id, ts = ts, msg = m);
                        }

                        // Left/removed participants
                        else if (delta["leftParticipantFbId"] != null)
                        {
                            // removed_id = str(delta['leftParticipantFbId']);
                            // thread_id = str(metadata['threadKey']['threadFbId']);
                            // this.onPersonRemoved(mid = mid, removed_id = removed_id, author_id = author_id, thread_id = thread_id, ts = ts, msg = m);
                        }

                        // Color change
                        else if (delta_type == "change_thread_theme")
                        {
                            // new_color = graphql_color_to_enum(delta["untypedData"]["theme_color"]);
                            // thread_id, thread_type = getThreadIdAndThreadType(metadata);
                            // this.onColorChange(mid = mid, author_id = author_id, new_color = new_color, thread_id = thread_id, thread_type = thread_type, ts = ts, metadata = metadata, msg = m);
                        }

                        // Emoji change
                        else if (delta_type == "change_thread_icon")
                        {
                            // new_emoji = delta["untypedData"]["thread_icon"];
                            // thread_id, thread_type = getThreadIdAndThreadType(metadata);
                            // this.onEmojiChange(mid = mid, author_id = author_id, new_emoji = new_emoji, thread_id = thread_id, thread_type = thread_type, ts = ts, metadata = metadata, msg = m);
                        }

                        // Thread title change
                        else if (delta["class"].Value<string>() == "ThreadName")
                        {
                            // new_title = delta["name"];
                            // thread_id, thread_type = getThreadIdAndThreadType(metadata);
                            // this.onTitleChange(mid = mid, author_id = author_id, new_title = new_title, thread_id = thread_id, thread_type = thread_type, ts = ts, metadata = metadata, msg = m);
                        }

                        // Nickname change
                        else if (delta_type == "change_thread_nickname")
                        {
                            // changed_for = str(delta["untypedData"]["participant_id"]);
                            // new_nickname = delta["untypedData"]["nickname"];
                            // thread_id, thread_type = getThreadIdAndThreadType(metadata);
                            // this.onNicknameChange(mid = mid, author_id = author_id, changed_for = changed_for, new_nickname = new_nickname, thread_id = thread_id, thread_type = thread_type, ts = ts, metadata = metadata, msg = m);
                        }

                        // Message delivered
                        else if (delta["class"].Value<string>() == "DeliveryReceipt")
                        {
                            // message_ids = delta["messageIds"];
                            // delivered_for = str(delta.get("actorFbId") or delta["threadKey"]["otherUserFbId"]);
                            // ts = int(delta["deliveredWatermarkTimestampMs"]);
                            // thread_id, thread_type = getThreadIdAndThreadType(delta);
                            // this.onMessageDelivered(msg_ids = message_ids, delivered_for = delivered_for, thread_id = thread_id, thread_type = thread_type, ts = ts, metadata = metadata, msg = m);
                        }

                        // Message seen
                        else if (delta["class"].Value<string>() == "ReadReceipt")
                        {
                            // seen_by = str(delta.get("actorFbId") or delta["threadKey"]["otherUserFbId"]);
                            // seen_ts = int(delta["actionTimestampMs"]);
                            // delivered_ts = int(delta["watermarkTimestampMs"]);
                            // thread_id, thread_type = getThreadIdAndThreadType(delta);
                            // this.onMessageSeen(seen_by = seen_by, thread_id = thread_id, thread_type = thread_type, seen_ts = seen_ts, ts = delivered_ts, metadata = metadata, msg = m);
                        }

                        // Messages marked as seen
                        else if (delta["class"].Value<string>() == "MarkRead")
                        {
                            // seen_ts = int(delta.get("actionTimestampMs") or delta.get("actionTimestamp"));
                            // delivered_ts = int(delta.get("watermarkTimestampMs") or delta.get("watermarkTimestamp"));

                            // threads = [];
                            // if ("folders" not in delta)
                            // {
                            // threads = [getThreadIdAndThreadType({ "threadKey": thr}) for thr in delta.get("threadKeys")];
                            // }

                            // thread_id, thread_type = getThreadIdAndThreadType(delta)
                            // this.onMarkedSeen(threads = threads, seen_ts = seen_ts, ts = delivered_ts, metadata = delta, msg = m);
                        }

                        // New message
                        else if (delta["class"].Value<string>() == "NewMessage")
                        {
                            var message = delta["body"] != null ? delta["body"].Value<string>() : "";
                            var id_type = getThreadIdAndThreadType(metadata);
                            this.onMessage(mid: mid, author_id: author_id, message: message,
                                thread_id: id_type.Item1, thread_type: id_type.Item2, ts: ts, metadata: metadata, msg: m);
                        }

                        // Unknown message type
                        else
                        {
                            this.onUnknownMesssageType(msg: m);
                        }
                    }

                    // Inbox
                    else if (mtype == "inbox")
                    {
                        this.onInbox(unseen: m["unseen"].Value<int>(), unread: m["unread"].Value<int>(), recent_unread: m["recent_unread"].Value<int>(), msg: m);
                    }

                    // Typing
                    // elif mtype == "typ":
                    //     author_id = str(m.get("from"))
                    //     typing_status = TypingStatus(m.get("st"))
                    //     this.onTyping(author_id=author_id, typing_status=typing_status)

                    // Delivered

                    // Seen
                    // elif mtype == "m_read_receipt":

                    //     this.onSeen(m.get('realtime_viewer_fbid'), m.get('reader'), m.get('time'))

                    // elif mtype in ['jewel_requests_add']:
                    //         from_id = m['from']
                    //         this.on_friend_request(from_id)

                    // Happens on every login
                    else if (mtype == "qprimer")
                    {
                        // this.onQprimer(ts: m.get("made"), msg: m);
                    }

                    // Is sent before any other message
                    else if (mtype == "deltaflow")
                    {

                    }

                    // Chat timestamp
                    else if (mtype == "chatproxy-presence")
                    {
                        var buddylist = new Dictionary<string, string>();
                        if (m["buddyList"] != null)
                        {
                            foreach (var payload in m["buddyList"].Value<JObject>().Properties())
                            {
                                buddylist[payload.Name] = payload.Value["lat"].Value<string>();
                            }
                            this.onChatTimestamp(buddylist: buddylist, msg: m);
                        }
                    }

                    // Unknown message type
                    else
                    {
                        this.onUnknownMesssageType(msg: m);
                    }
                }

                catch (Exception e)
                {
                    this.onMessageError(exception: e, msg: m);
                }
            }
        }

        public async Task startListening()
        {
            /*
             * Start listening from an external event loop
             * :raises: Exception if request failed
             */
            this.listening = true;
            var sticky_pool = await this._fetchSticky();
            this.sticky = sticky_pool.Item1;
            this.pool = sticky_pool.Item2;
        }

        public async Task<bool> doOneListen(bool markAlive = true)
        {
            /*
             * Does one cycle of the listening loop.
             * This method is useful if you want to control fbchat from an external event loop
             * :param markAlive: Whether this should ping the Facebook server before running
             * :type markAlive: bool
             * :return: Whether the loop should keep running
             * :rtype: bool
             */

            try
            {
                if (markAlive) await this._ping(this.sticky, this.pool);
                try
                {
                    var content = await this._pullMessage(this.sticky, this.pool);
                    if (content != null) this._parseMessage(content);
                }
                // catch (requests.exceptions.RequestException)
                // {
                // pass;
                // }
                catch (Exception e)
                {
                    this.onListenError(exception: e);
                }
            }
            catch (Exception)
            {

            }

            return true;
        }

        public void stopListening()
        {
            /*Cleans up the variables from startListening*/
            this.listening = false;
            this.sticky = null;
            this.pool = null;
        }

        /*
        END LISTEN METHODS
        */

        /*
         * EVENTS
         */

        // An event that clients can use to be notified whenever the
        // elements of the list change.
        public event EventHandler<LoginEventArgs> LoginEvent;
        public event EventHandler<UpdateEventArgs> UpdateEvent;

        protected void OnLoginEvent(LoginEventArgs e)
        {
            LoginEvent?.Invoke(this, e);
        }

        protected void OnUpdateEvent(UpdateEventArgs e)
        {
            UpdateEvent?.Invoke(this, e);
        }

        protected void onListening()
        {
            /*Called when the client is listening*/
            Debug.WriteLine("Listening...");
        }

        protected void onListenError(Exception exception = null)
        {
            /*
             * Called when an error was encountered while listening
             * :param exception: The exception that was encountered
             */
            Debug.WriteLine(string.Format("Got exception while listening: {0}", exception));
        }

        protected void onChatTimestamp(Dictionary<string, string> buddylist = null, JToken msg = null)
        {
            /*
             * Called when the client receives chat online presence update
             * :param buddylist: A list of dicts with friend id and last seen timestamp
             * :param msg: A full set of the data recieved
             */
            Debug.WriteLine(string.Format("Chat Timestamps received: {0}", buddylist));
        }

        private void onInbox(int unseen, int unread, int recent_unread, JToken msg)
        {
            Debug.WriteLine(string.Format("Inbox event: {0}, {1}, {2}", unseen, unread, recent_unread));
        }

        protected void onMessage(string mid = null, string author_id = null, string message = null, string thread_id = null, ThreadType thread_type = ThreadType.USER, string ts = null, JToken metadata = null, JToken msg = null)
        {
            /*
            Called when the client is listening, and somebody sends a message
            :param mid: The message ID
            :param author_id: The ID of the author
            :param message: The message
            :param thread_id: Thread ID that the message was sent to.See :ref:`intro_threads`
            :param thread_type: Type of thread that the message was sent to.See :ref:`intro_threads`
            :param ts: The timestamp of the message
            :param metadata: Extra metadata about the message
            :param msg: A full set of the data recieved
            :type thread_type: models.ThreadType
            */
            UpdateEvent(this, new UpdateEventArgs(UpdateStatus.NEW_MESSAGE, new Message(mid, author_id, ts, false, null, message)));
            Debug.WriteLine(string.Format("Message from {0} in {1} ({2}): {3}", author_id, thread_id, thread_type.ToString(), message));
        }

        protected void onUnknownMesssageType(JToken msg = null)
        {
            /*
             * Called when the client is listening, and some unknown data was recieved
             * :param msg: A full set of the data recieved
             */
            Debug.WriteLine(string.Format("Unknown message received: {}", msg));
        }

        protected void onMessageError(Exception exception = null, JToken msg = null)
        {
            /*
             * Called when an error was encountered while parsing recieved data
             * :param exception: The exception that was encountered
             * :param msg: A full set of the data recieved
             */
            Debug.WriteLine(string.Format("Exception in parsing of {0}", msg));
        }

        /*
         * END EVENTS
         */

        public async Task<bool> markAsDelivered(string userID, string threadID)
        {
            /*
             * ..todo::
             * Documenting this
             */
            var data = new Dictionary<string, string>() {
                { "message_ids[0]", threadID },
                { string.Format("thread_ids[{0}][0]", userID), threadID}
            };

            var r = await this._post(ReqUrl.DELIVERED, data);
            return r.IsSuccessStatusCode;
        }

        public async Task<bool> markAsRead(string userID)
        {
            /*
             * ..todo::
             * Documenting this
             */
            var data = new Dictionary<string, string>() {
                { "watermarkTimestamp", Utils.now().ToString() },
                { "shouldSendReadReceipt", true.ToString()},
                { string.Format("ids[{0}]", userID), true.ToString()}
            };

            var r = await this._post(ReqUrl.READ_STATUS, data);
            return r.IsSuccessStatusCode;
        }

        public async Task<bool> markAsSeen()
        {
            /*
             * ..todo::
             * Documenting this
             */
            var data = new Dictionary<string, string>()
            {
                { "seen_timestamp", 0.ToString()}
            };

            var r = await this._post(ReqUrl.MARK_SEEN, data);
            return r.IsSuccessStatusCode;
        }

        public async Task<bool> friendConnect(string friend_id)
        {
            /*
             * ..todo::
             * Documenting this
             */
            var data = new Dictionary<string, string>()
            {
                { "to_friend", friend_id },
                {"action", "confirm" }
            };

            var r = await this._post(ReqUrl.CONNECT, data);
            return r.IsSuccessStatusCode;
        }
    }
}