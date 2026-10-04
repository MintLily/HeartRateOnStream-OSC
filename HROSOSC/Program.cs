using WebSocketSharp;
using WebSocketSharp.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Sockets;
using BuildSoft.VRChat.Osc;
using System.Net.NetworkInformation;
using ErrorEventArgs = WebSocketSharp.ErrorEventArgs;

namespace HROSOSC;

internal static class Program {
    private static WebSocketServer? _server;
#if DEBUG
    private const int Port = 8999;
#else
    private const int Port = 4456;
#endif
    private static bool _connected;
    private static string _requestType = "", _requestId = "";
    private static int _lastHeartrate, _heartrate;
    private static readonly Queue<string> MessagesToSend = new();

    private static void Main(string[] args) {
        _server = new WebSocketServer(Port);
        _server.Start();
        _server.AddWebSocketService<HeartrateSocket>("/");

        Console.Title = "HeartRateOnStream OSC";
        Console.WriteLine("HeartRateOnStream OSC - VRChat Wrapper by Curtis-VL modified by Lily");
        Console.WriteLine();
        Console.WriteLine("Please use the following connection details in the HeartRateOnStream app...");
        Console.WriteLine("===========================================================================================");
        Console.WriteLine("Computer's IP: " + GetLocalIpAddress());
        Console.WriteLine("Communication Port: " + Port);
        Console.WriteLine("\t* You can set this in the Autodiscover box!");
        // Console.WriteLine("Password: (leave the box blank)");

        while (true) {
            while (MessagesToSend.Count > 0) {
                var response = MessagesToSend.Dequeue();
                _server?.WebSocketServices["/"].Sessions.Broadcast(response);
            }

            if (_connected) {
                OscParameter.SendAvatarParameter("isHRConnected", true);

                // Respond with a success message to client requests.
                if (_requestId != "") {
                    _server?.WebSocketServices["/"].Sessions.Broadcast("{\"op\": 7, \"d\": { \"requestType\": " + _requestType + ", \"requestId\": " + _requestId + ", \"requestStatus\": { \"result\": true, \"code\": 100 }}}");
                    _requestId = "";
                }

                // When heartrate value has changed, send to OSC.
                if (_lastHeartrate != _heartrate) {
                    _lastHeartrate = _heartrate;

                    OscParameter.SendAvatarParameter("Heartrate", Remap(_heartrate, 0, 255, -1, 1));
                    OscParameter.SendAvatarParameter("Heartrate2", Remap(_heartrate, 0, 255, 0, 1));
                    OscParameter.SendAvatarParameter("Heartrate3", _heartrate);

                    OscParameter.SendAvatarParameter("HR", _heartrate);
                    OscParameter.SendAvatarParameter("isHRActive", true);
                }
            }
            else {
                OscParameter.SendAvatarParameter("isHRConnected", false);
                OscParameter.SendAvatarParameter("isHRActive", false);
            }

            Thread.Sleep(100);
        }
        // ReSharper disable once FunctionNeverReturns
    }

    /// <summary>
    /// Remap a value to a different range.
    /// </summary>
    /// <param name="value">Value to map.</param>
    /// <param name="leftMin">Original range min.</param>
    /// <param name="leftMax">Original range max.</param>
    /// <param name="rightMin">New range min.</param>
    /// <param name="rightMax">New range max.</param>
    /// <returns>remapped float conversion to an integer</returns>
    private static float Remap(float value, float leftMin, float leftMax, float rightMin, float rightMax) 
        => rightMin + (value - leftMin) * (rightMax - rightMin) / (leftMax - leftMin);

    /// <summary>
    /// Web socket behaviour for all incoming messages from HeartRateOnStream.
    /// </summary>
    private class HeartrateSocket : WebSocketBehavior {
        //public void SendMessage(string contents) {
        //    SendMessage(contents);
        //}

        protected override void OnOpen() {
            Console.Clear();
            _connected = true;

            // Send hello. (op 0)
            const string response = "{\"op\": 0, \"d\": {\"obsWebSocketVersion\": \"5.4.2\", \"rpcVersion\": 1}}";
            MessagesToSend.Enqueue(response);

            Console.Title = "HeartRateOnStream OSC - Connected to Phone";
            Console.Clear();
            Console.WriteLine("HeartRateOnStream OSC - VRChat Wrapper by Curtis-VL modified by Lily");
            Console.WriteLine("");
            Console.WriteLine("App connected! Continue to the next page in the app.");
        }

        protected override void OnMessage(MessageEventArgs e) {
            var obj = JObject.Parse(e.Data);
            var opCode = (int)obj["op"]!;

            switch (opCode) {
                // Identify
                case 1: {
                    const string response = "{\"d\": {\"negotiatedRpcVersion\": 1 }, \"op\": 2}";
                    MessagesToSend.Enqueue(response);
                    break;
                }
                // Request.
                // OP Code 6 is a request.
                case 6 when (int)obj["op"]! != 6:
                    return;
                case 6: {
                    _requestId = (string)obj["d"]?["requestId"]!;
                    _requestType = (string)obj["d"]?["requestType"]!;

                    // SetInputSettings request, should always be the heartrate.
                    if (_requestType == "SetInputSettings") {
                        try {
                            var inputName = (string)obj["d"]?["requestData"]?["inputName"]!;
                            if (inputName == "heartrate") {
                                _heartrate = Convert.ToInt32((string)obj["d"]?["requestData"]?["inputSettings"]?["text"]!);
                                Console.Title = "HeartRateOnStream OSC - Connected to Phone - Heart Rate: " + _heartrate;
                                Console.Clear();
                                Console.WriteLine("HeartRateOnStream OSC - VRChat Wrapper by Curtis-VL modified by Lily");
                                Console.WriteLine("");
                                Console.WriteLine("Heart Rate: " + _heartrate);
                                Console.WriteLine("\t* targeting '/avatar/parameters/Heartrate' as an INT");
                            }
                            else {
                                var response = "{\"d\": { \"requestId\": \"0dc1d282-84a1-4683-9bdd-1cebc25e4d1b\", \"requestStatus\": { \"code\": 600, \"comment\": \"No source was found by the name of `" + inputName + "`.\", \"result\": false }, \"requestType\": \"SetInputSettings\" }, \"op\": 7}";
                                MessagesToSend.Enqueue(response);
                            }
                        }
                        catch {
                            // Or maybe it's not always the heart rate.
                            // That's why this is here.
                        }
                    }
                    else if (_requestType == "GetSceneList") {
                        var sceneListResponse = new JObject {
                            ["d"] = new JObject {
                                ["requestId"] = (string)obj["d"]?["requestId"]!,
                                ["requestStatus"] = new JObject {
                                    ["code"] = 100,
                                    ["result"] = true,
                                },
                                ["requestType"] = "GetSceneList",
                                ["responseData"] = new JObject {
                                    ["currentPreviewSceneName"] = null,
                                    ["currentPreviewSceneUuid"] = null,
                                    ["currentProgramSceneName"] = "HeartRateOnStream-OSC",
                                    ["currentProgramSceneUuid"] = "755bb54b-c681-44a2-bc0d-da59d534f4af",
                                    ["scenes"] = new JArray {
                                        new JObject {
                                            ["sceneIndex"] = 0,
                                            ["sceneName"] = "HeartRateOnStream-OSC",
                                            ["sceneUuid"] = "88b2711a-f268-4405-a150-a82331db2b48"
                                        }
                                    }
                                }
                            },
                            ["op"] = 7,
                        };
                        MessagesToSend.Enqueue(JsonConvert.SerializeObject(sceneListResponse));
                    }
                    else if (_requestType == "GetCurrentProgramScene") {
                        var getCurrentProgramResponse = new JObject {
                            ["d"] = new JObject {
                                ["requestId"] = (string)obj["d"]?["requestId"]!,
                                ["requestStatus"] = new JObject {
                                    ["code"] = 100,
                                    ["result"] = true,
                                },
                                ["requestType"] = "GetCurrentProgramScene",
                                ["responseData"] = new JObject {
                                    ["currentProgramSceneName"] = "HeartRateOnStream-OSC",
                                    ["currentProgramSceneUuid"] = "755bb54b-c681-44a2-bc0d-da59d534f4af",
                                    ["sceneName"] = "HeartRateOnStream-OSC",
                                    ["sceneUuid"] = "755bb54b-c681-44a2-bc0d-da59d534f4af"
                                }
                            },
                            ["op"] = 7,
                        };
                        MessagesToSend.Enqueue(JsonConvert.SerializeObject(getCurrentProgramResponse));
                    }
                    else if (_requestType == "GetSceneItemList") {
                        var response = "{ \"d\": { \"requestId\": \"" + (string)obj["d"]?["requestId"]!;
                        const string s = "\", \"requestStatus\": { \"code\": 100, \"result\": true }, \"requestType\": \"GetSceneItemList\", \"responseData\": { \"sceneItems\": [ { \"inputKind\": \"text_gdiplus_v2\", \"isGroup\": null, \"sceneItemBlendMode\": \"OBS_BLEND_NORMAL\", \"sceneItemEnabled\": true, \"sceneItemId\": 1, \"sceneItemIndex\": 0, \"sceneItemLocked\": false, \"sceneItemTransform\": { \"alignment\": 5, \"boundsAlignment\": 0, \"boundsHeight\": 0.0, \"boundsType\": \"OBS_BOUNDS_NONE\", \"boundsWidth\": 0.0, \"cropBottom\": 0, \"cropLeft\": 0, \"cropRight\": 0, \"cropTop\": 0, \"height\": 256.0, \"positionX\": 0.0, \"positionY\": 0.0, \"rotation\": 0.0, \"scaleX\": 1.0, \"scaleY\": 1.0, \"sourceHeight\": 256.0, \"sourceWidth\": 2.0, \"width\": 2.0 }, \"sourceName\": \"heartrate\", \"sourceType\": \"OBS_SOURCE_TYPE_INPUT\", \"sourceUuid\": \"ab5bb7b9-7ef9-4bc0-b4c5-2d01a47a7ee0\" } ] } }, \"op\": 7}";
                        MessagesToSend.Enqueue(response + s);

                        Console.Clear();
                        Console.WriteLine("HeartRateOnStream-OSC");
                        Console.WriteLine("");
                        Console.WriteLine("Select the 'heartrate' text in the Source selection and select 'As heartrate'!");
                        Console.WriteLine("Then, click 'Finish selection'!");
                        Console.WriteLine("");
                        Console.WriteLine("(It can take 30s~ to update after clicking finish, be patient!)");
                    }

                    break;
                }
            }
        }

        protected override void OnClose(CloseEventArgs e) {
            Console.WriteLine("Socket closed: " + e.Reason + ", Clean close: " + e.WasClean);
            _connected = false;
            base.OnClose(e);
        }

        protected override void OnError(ErrorEventArgs e) {
            Console.WriteLine("Socket error: " + e.Message);
            _connected = false;
            base.OnError(e);
        }
    }

    /// <summary>
    /// Get the local IP address of the machine.
    /// </summary>
    private static string GetLocalIpAddress() {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()) {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                continue;

            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;

            var properties = networkInterface.GetIPProperties();

            // Only consider adapters with a default gateway.
            if (properties.GatewayAddresses.Count == 0)
                continue;

            foreach (var address in properties.UnicastAddresses) {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                
                if (address.Address.ToString().StartsWith("169.254."))
                    continue;

                return address.Address.ToString();
            }
        }

        return "Disconnected - Check your network connection!";
    }
}