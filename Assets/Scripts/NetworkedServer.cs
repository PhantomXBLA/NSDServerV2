using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491; //change back to 5491

    bool canSend = true;

    LinkedList<PlayerAccount> playerAccounts;

    const int PlayerAccountNamePassword = 1;

    string playerAccountsFilePath;

    int playerWaitingForMatchWithID = -1;

    LinkedList<GameRoom> gameRooms;

    int playerTurn = 1;

    LinkedList<string> replayMoves;

    float replayMoveTimer = 0;
    float desiredDelayLength = 3.5f;
    

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        playerAccountsFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt";
        playerAccounts = new LinkedList<PlayerAccount>();


        LoadPlayerAccounts();

        foreach (PlayerAccount pa in playerAccounts)
            Debug.Log(pa.username + " " + pa.password);

        gameRooms = new LinkedList<GameRoom>();

        replayMoves = new LinkedList<string>();



    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }

    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] csv = msg.Split(',');

        int signifier = int.Parse(csv[0]);



        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
            Debug.Log("create account");

            string n = csv[1];
            string p = csv[2];

            bool nameUsed = false;

            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.username == n)
                    nameUsed = true;
                    break;
            }

            if (nameUsed)
            {
                SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + "", id);
            }
            else
            {
                PlayerAccount newPlayerAccount = new PlayerAccount(n, p);
                playerAccounts.AddLast(newPlayerAccount);
                SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + "", id);

                SavePlayerAccounts();



            }

        }
        else if (signifier == ClientToServerSignifiers.Login)
        {
            Debug.Log("login to account");

            string n = csv[1];
            string p = csv[2];
            bool hasNameBeenFound = false;
            bool msgSentToClient = false;

            foreach (PlayerAccount pa in playerAccounts)
            {
                if(pa.username == n)
                {
                    hasNameBeenFound = true;
                    if(pa.password == p)
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginComplete + "", id);
                        msgSentToClient = true;


                    }
                    else
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
                        msgSentToClient = true;
                    }
                }
            }


            if (!hasNameBeenFound && !msgSentToClient)
            {
                    SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
            }

        }
        else if (signifier == ClientToServerSignifiers.JoinGameRoomQueue)
        {
            Debug.Log("queue joined baby");

            if (playerWaitingForMatchWithID == -1)
            {
                playerWaitingForMatchWithID = id;
            }

            else if (playerWaitingForMatchWithID == -2) // we need an observer to join the game, after starting the game, we set the playerwaitingformatchwithID variable to -2 to trigger this code which allows the observer to join the game
            {
                GameRoom gr = GetGameRoomWithClientID(id - 1);
                gr.observer = id;
                SendMessageToClient(ServerToClientSignifiers.GameStart + "," + 3, gr.observer); // calls the gamestart for the observer

                playerWaitingForMatchWithID = -1; //sets it back to -1 so another game can start

            }

            else
            {
                GameRoom gr = new GameRoom(playerWaitingForMatchWithID, id);
                gameRooms.AddLast(gr);

                SendMessageToClient(ServerToClientSignifiers.GameStart + "," + 1, gr.playerID1);   //starts the game for both clients in queue
                SendMessageToClient(ServerToClientSignifiers.GameStart + "," + 2, gr.playerID2);   //starts the game for both clients in queue




                playerWaitingForMatchWithID = -2;
            }

            
        }
        else if (signifier == ClientToServerSignifiers.InGame)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            int GameSignifier = int.Parse(csv[1]);

            if (gr != null)
            {
                if(gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay +"", gr.playerID2);
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "", gr.playerID1);
                }

                if (GameSignifier == ChatSignifiers.PremadeMessage)
                {
                    string premadeMessage = csv[2];
                    Debug.Log(premadeMessage);

                    int playerID = id;

                    if (gr.playerID1 == id) 
                    {
                        SendMessageToClient(ClientToServerSignifiers.InGame + "," + ChatSignifiers.PremadeMessage + "," + premadeMessage + "," + playerID, gr.playerID2);     //sends the premade messages to p1 and p2 but not the observer
                        SendMessageToClient(ClientToServerSignifiers.InGame + "," + ChatSignifiers.PremadeMessage + "," + premadeMessage + "," + playerID, gr.playerID1);     //sends the premade messages to p1 and p2 but not the observer


                    }
                    else
                    {
                        SendMessageToClient(ClientToServerSignifiers.InGame + "," + ChatSignifiers.PremadeMessage + "," + premadeMessage + "," + playerID, gr.playerID1);  //sends the premade messages to p1 and p2 but not the observer
                        SendMessageToClient(ClientToServerSignifiers.InGame + "," + ChatSignifiers.PremadeMessage + "," + premadeMessage + "," + playerID, gr.playerID2);  //sends the premade messages to p1 and p2 but not the observer
                    }

                } 
                
                else if(GameSignifier == ChatSignifiers.Message)
                {
                    string message = csv[2];
                    int playerID = id;

                    if (gr.playerID1 == id)
                    {
                        SendMessageToClient(ClientToServerSignifiers.InGame + "," + ChatSignifiers.Message + "," + message + "," + playerID, gr.playerID2);
                        SendMessageToClient(ClientToServerSignifiers.InGame + "," + ChatSignifiers.Message + "," + message + "," + playerID, gr.playerID1);


                    }
                    else
                    {
                        SendMessageToClient(ClientToServerSignifiers.InGame + "," + ChatSignifiers.Message + "," + message + "," + playerID, gr.playerID1);
                        SendMessageToClient(ClientToServerSignifiers.InGame + "," + ChatSignifiers.Message + "," + message + "," + playerID, gr.playerID2);
                    }
                }

                if (GameSignifier == GameSignifiers.PlayerMoved)
                {
               
                    string buttonName = csv[2];
                    float posX = float.Parse(csv[3]);
                    float posY = float.Parse(csv[4]);
                    int playerID = id;

                    if (gr.playerID1 == id)
                    {

                        if(playerTurn == PlayerIDCheck.PlayerID1)
                        {
                            Debug.Log("MoveFromP1");
                            SendMessageToClient(ClientToServerSignifiers.InGame + "," + GameSignifiers.PlayerMoved + "," + posX + "," + posY + "," + buttonName + "," + playerID, gr.playerID2);      //sends the move at the received position and button back to the client and the observer
                            SendMessageToClient(ClientToServerSignifiers.InGame + "," + GameSignifiers.PlayerMoved + "," + posX + "," + posY + "," + buttonName + "," + playerID, gr.playerID1);      //sends the move at the received position and button back to the client and the observer
                                                                                                                                                                                                    
                            SendMessageToClient(ClientToServerSignifiers.InGame + "," + GameSignifiers.PlayerMoved + "," + posX + "," + posY + "," + buttonName + "," + playerID, gr.observer);       //sends the move at the received position and button back to the client and the observer


                            string move = buttonName;
                            replayMoves.AddLast(move);


                            playerTurn = PlayerIDCheck.PlayerID2;

                        }



                    }
                    else if(gr.playerID2 == id)
                    {

                        if(playerTurn == PlayerIDCheck.PlayerID2)
                        {
                            Debug.Log("MoveFromP2");
                            SendMessageToClient(ClientToServerSignifiers.InGame + "," + GameSignifiers.PlayerMoved + "," + posX + "," + posY + "," + buttonName + "," + playerID, gr.playerID1);
                            SendMessageToClient(ClientToServerSignifiers.InGame + "," + GameSignifiers.PlayerMoved + "," + posX + "," + posY + "," + buttonName + "," + playerID, gr.playerID2);

                            SendMessageToClient(ClientToServerSignifiers.InGame + "," + GameSignifiers.PlayerMoved + "," + posX + "," + posY + "," + buttonName + "," + playerID, gr.observer);



                            string move = buttonName;
                            replayMoves.AddLast(move);

                            playerTurn = PlayerIDCheck.PlayerID1;
                        }


                    }
                }
            }
        }
        
        else if(signifier == ClientToServerSignifiers.WinForX)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            SendMessageToClient(ClientToServerSignifiers.InGame + "," + ClientToServerSignifiers.WinForX, gr.playerID1);
            SendMessageToClient(ClientToServerSignifiers.InGame + "," + ClientToServerSignifiers.WinForX, gr.playerID2);
            playerTurn = PlayerIDCheck.PlayerIDNull;

        }

        else if (signifier == ClientToServerSignifiers.WinForO)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            SendMessageToClient(ClientToServerSignifiers.InGame + "," + ClientToServerSignifiers.WinForO, gr.playerID1);
            SendMessageToClient(ClientToServerSignifiers.InGame + "," + ClientToServerSignifiers.WinForO, gr.playerID2);
            playerTurn = PlayerIDCheck.PlayerIDNull;

        }




        else if(signifier == ClientToServerSignifiers.JoinReplay)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            Debug.Log("we in the replay now");
            playerTurn = PlayerIDCheck.PlayerIDNull;
            SendMessageToClient(ServerToClientSignifiers.GameEnd + "", gr.playerID1);
            SendMessageToClient(ServerToClientSignifiers.GameEnd + "", gr.playerID2);

            Debug.Log("moves: " + replayMoves.Count);


            for (int i = 0; i < replayMoves.Count + 1;)
            {
                if (replayMoves.Count > 0)
                {
                    string replayMsg;
                    replayMsg = replayMoves.First.Value;

                    replayMoves.RemoveFirst();


                    SendMessageToClient(ServerToClientSignifiers.SendReplay + "," + replayMsg, gr.playerID1);
                    SendMessageToClient(ServerToClientSignifiers.SendReplay + "," + replayMsg, gr.playerID2);
                    canSend = false;

                    while (replayMoveTimer < desiredDelayLength)
                    {
                        replayMoveTimer += (Time.deltaTime / desiredDelayLength);
                        Debug.Log(replayMoveTimer);

                        if (replayMoveTimer >= desiredDelayLength)
                        {
                            canSend = true;
                            replayMoveTimer = 0;
                            break;
                        }

                    }
                }
                else
                {
                    break;
                }

            } 
        }


    }

    private void SavePlayerAccounts()
    {
        StreamWriter sw = new StreamWriter(playerAccountsFilePath);

        foreach(PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(PlayerAccountNamePassword + "," + pa.username + "," + pa.password);
        }

        sw.Close();
    }

    private void LoadPlayerAccounts()
    {
        if (File.Exists(playerAccountsFilePath))
        {

            StreamReader sr = new StreamReader(playerAccountsFilePath);

            string line;

            while ((line = sr.ReadLine()) != null)
            {
                 string[] csv = line.Split(',');
                 int signifier = int.Parse(csv[0]);

                if(signifier == PlayerAccountNamePassword)
                {
                    PlayerAccount pa = new PlayerAccount(csv[1], csv[2]);
                    playerAccounts.AddLast(pa);
                }
            }
        sr.Close();
        }
    }

    private GameRoom GetGameRoomWithClientID(int id)
    {
        foreach(GameRoom gr in gameRooms)
        {
            if(gr.playerID1 == id || gr.playerID2 == id)
            {
                return gr;
            }

            
        }
        return null;
    }

}




public class PlayerAccount
{
    public string username;
    public string password;


    public PlayerAccount(string Username, string Password)
    {
        username = Username;
        password = Password;
    }
}

public class GameRoom
{
    public int playerID1, playerID2, observer;

    public GameRoom(int PlayerID1, int PlayerID2)
    {
        playerID1 = PlayerID1;
        playerID2 = PlayerID2;
        //observer = Observer;

    }
}

public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;

    public const int JoinGameRoomQueue = 3;
    public const int InGame = 4;

    public const int JoinReplay = 5;

    public const int WinForX = 6;
    public const int WinForO = 7;
    public const int Tie = 8;

}

public static class GameSignifiers
{
    public const int PlayerMoved = 1;
}

public static class ChatSignifiers
{
    public const int PremadeMessage = 2;
    public const int Message = 3;
}
public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;

    public const int AccountCreationComplete = 3;
    public const int AccountCreationFailed = 4;

    public const int OpponentPlay = 5;

    public const int GameStart = 6;
    public const int GameEnd = 7;

    public const int SendReplay = 8;



}

public static class PlayerIDCheck
{

    public const int PlayerIDNull = 0;
    public const int PlayerID1 = 1;
    public const int PlayerID2 = 2;
    public const int ObserverID = 3;
}


