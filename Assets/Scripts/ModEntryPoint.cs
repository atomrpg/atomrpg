﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using JSon;
using System.Reflection;
using System.Runtime.CompilerServices;

//[assembly: AssemblyTitle("My Mod")] // ENTER MOD TITLE

namespace OnlineEvents
{
    public struct Login
    {
        public int uid;
        public int room;
        public int x, y;
        public int lastActionId;
        public JSon.JNode data;
    }

    public struct Travel
    {
        public int dir;
    }

    public struct ChatSend
    {
        public string msg;
    }

    public struct ChatGet
    {
        public string msg;
        public int initiator;
    }
}

public class ModEntryPoint : MonoBehaviour // ModEntryPoint - RESERVED LOOKUP NAME
{
    private bool _inBattle = false;
    private bool _signed = false;
    private bool _fetch = false;
    private int _uid = 0;
    private int _room = 0;
    private int _lastActionId = 0;
    public GameObject _loginForm;
    Dictionary<int, CharacterComponent> _characters = new Dictionary<int, CharacterComponent>();

    public static string server = "http://online.theatomgame.com/";

    public void SetMessagesFetch(bool fetch)
    {
        _fetch = fetch;
    }

    void Start()
    {
        var assembly = GetType().Assembly;
        string modName = assembly.GetName().Name;
        string dir = System.IO.Path.GetDirectoryName(assembly.Location);
        Debug.Log("Mod Init: " + modName + "(" + dir + ")");
        ResourceManager.AddBundle(modName, AssetBundle.LoadFromFile(dir + "/" + modName + "_resources"));
        GlobalEvents.AddListener<GlobalEvents.GameStart>(GameLoaded);
        GlobalEvents.AddListener<GlobalEvents.LevelLoaded>(LevelLoaded);

        GlobalEvents.AddListener<OnlineEvents.Login>(OnLogin);
        GlobalEvents.AddListener<GlobalEvents.LevelLoaded>(OnLevelLoaded);
        GlobalEvents.AddListener<GlobalEvents.CharacterMove>(OnCharacterMove);
        GlobalEvents.AddListener<GlobalEvents.CharacterTurnEnd>(OnCharacterTurnEnd);
        GlobalEvents.AddListener<GlobalEvents.CharacterOnAttack>(OnCharacterOnAttack);
        GlobalEvents.AddListener<OnlineEvents.ChatSend>(OnChat);
        GlobalEvents.AddListener<OnlineEvents.Travel>(OnTravel);

        _loginForm = ResourceManager.Load<GameObject>("LoginForm", ResourceManager.EXT_PREFAB);
    }

    void GameLoaded(GlobalEvents.GameStart evnt)
    {
        //Localization.LoadStrings("mymod_strings_");
        Game.World.console.DeveloperMode();
    }

    void OnTravel(OnlineEvents.Travel evnt)
    {
        StartCoroutine(TryTravel(evnt.dir));
    }

    IEnumerator TryTravel(int dir)
    {
        WebRequest request = new WebRequest();
        yield return request.Do(server + "worldmap_travel.php",
            new MultipartFormDataSection("uid", _uid.ToString()),
            new MultipartFormDataSection("dir", dir.ToString()),
            new MultipartFormDataSection("room", _room.ToString()));
    }

    void LevelLoaded(GlobalEvents.LevelLoaded evnt)
    {
        Debug.Log(evnt.levelName);
    }

    void OnCharacterOnAttack(GlobalEvents.CharacterOnAttack evnt)
    {
        Random.InitState(0);
    }

    void OnCharacterTurnEnd(GlobalEvents.CharacterTurnEnd evnt)
    {
        if (Game.World.Player.CharacterComponent == evnt.cc)
        {
            StartCoroutine(TryTurnEnd());
        }
    }

    IEnumerator TryTurnEnd()
    {
        WebRequest request = new WebRequest();
        yield return request.Do(server + "character_turnend.php",
            new MultipartFormDataSection("uid", _uid.ToString()),
            new MultipartFormDataSection("room", _room.ToString()));
    }

    void OnChat(OnlineEvents.ChatSend chat)
    {
        StartCoroutine(TryChatSend(chat.msg));
    }

    IEnumerator TryChatSend(string msg)
    {
        WebRequest request = new WebRequest();
        yield return request.Do(server + "chat_send.php",
            new MultipartFormDataSection("uid", _uid.ToString()),
            new MultipartFormDataSection("room", _room.ToString()),
            new MultipartFormDataSection("msg", msg)
            );
    }

    void OnLogin(OnlineEvents.Login evnt)
    {
        _signed = true;
        _uid = evnt.uid;
        _room = evnt.room;
        _lastActionId = evnt.lastActionId;

        //Game.World.NextLevel("Z_1", "EnterPoint", false, false);
    }

    private void Whoop(CharacterComponent cc, string whoop, bool fromPlayer)
    {
        Game.World.HUD.Whoop(whoop, new Vector3(0, 5, 0), cc.transform, fromPlayer ? PlayerHUD.DefaultWhoopColor : PlayerHUD.FrendlyFireWhoopColor);
    }

    IEnumerator TryGetActions()
    {
        WebRequest request = new WebRequest();
        yield return request.Do(server + "actions_get.php",
            new MultipartFormDataSection("room", _room.ToString()),
            new MultipartFormDataSection("aid", _lastActionId.ToString())
            );

        _actionTimer = 2.0f;
        if (request.Success)
        {
            foreach (JSon.JNode jAction in request.GetData().AsArray)
            {
                _lastActionId = jAction["id"].AsInt;
                int type = jAction["type"].AsInt;
                int initiator = jAction["initiator"].AsInt;
                JSon.JNode data = JSon.JParser.Parse(jAction["data"]);

                if (type == 0) // move
                {
                    if (initiator == _uid)
                    {
                        continue;
                    }
                    var n = Pathfinder.Instance.FindNodeByCell(data["x"].AsInt, data["y"].AsInt);
                    if (n != null)
                    {
                        GetCC(initiator).MoveTo(n.GetPosition());
                    }
                }

                if (type == 1) // chat
                {
                    GlobalEvents.PerformEvent(new OnlineEvents.ChatGet(){ msg = data["msg"], initiator = initiator});
                    if(_inBattle)
                    {
                        Whoop(GetCC(initiator), data["msg"], initiator == _uid);
                    }
                }

                if (type == 2) //turn end
                {
                    GetCC(initiator).Character.AP = 0;
                }
            }
        }
        else
        {
            //error handle
        }
    }

    CharacterComponent GetCC(int id)
    {
        if (id == _uid)
        {
            return Game.World.Player.CharacterComponent;
        }
        else
        {
            return _characters[id];
        }
    }

    IEnumerator TryCharacterMove(Vector2Int xy)
    {
        WebRequest request = new WebRequest();
        yield return request.Do(server + "character_move.php",
            new MultipartFormDataSection("uid", _uid.ToString()),
            new MultipartFormDataSection("x", xy.x.ToString()),
            new MultipartFormDataSection("y", xy.y.ToString())
            );

        if (request.Success)
        {

        }
        else
        {
            //error handle
        }
    }

    IEnumerator TryGetCharacters(int room)
    {
        WebRequest request = new WebRequest();
        yield return request.Do(server + "characters_get.php",
            new MultipartFormDataSection("room", room.ToString())
            );

        _fetch = true;
        _inBattle = true;
        if (request.Success)
        {
            foreach (JSon.JNode jData in request.GetData().AsArray)
            {
                int uid = jData["uid"].AsInt;
                int x = jData["x"].AsInt;
                int y = jData["y"].AsInt;

                CharacterComponent cc;
                if (uid != _uid) //skip player
                {
                    GameObject copy = GameObject.Instantiate(ResourceManager.Load<GameObject>("Entities/Creature/BaseMale11", ResourceManager.EXT_PREFAB), Vector3.zero, Quaternion.identity);
                    cc = copy.GetComponent<CharacterComponent>();
                    cc.Character.Caps = Character.CharacterCaps.Custom;
                    var controller = new NetworkControl(uid);
                    cc.Controller = controller;
                    controller.Start(cc);
                    _characters.Add(uid, cc);
                }
                else
                {
                    cc = Game.World.Player.CharacterComponent;
                }

                var n = Pathfinder.Instance.FindNodeByCell(x, y);
                if (n != null)
                {
                    cc.Teleport(n.GetPosition(), false);
                    Game.World.Player.CameraSnap = true;
                }
            }
        }
        else
        {
            //error handle
        }
    }


    void OnCharacterMove(GlobalEvents.CharacterMove evnt)
    {
        if (Game.World.Player.CharacterComponent == evnt.cc)
        {
            StartCoroutine(TryCharacterMove(evnt.cell));
        }
    }

    void OnLevelLoaded(GlobalEvents.LevelLoaded evnt)
    {
        StartCoroutine(TryGetCharacters(0));
    }

    float _actionTimer = 2;
    // Update is called once per frame
    void Update()
    {
        if (_loginForm && GameObject.Find("MainMenu_HUD(Clone)"))
        {
            var mainMenu = GameObject.Find("MainMenu_HUD(Clone)").transform.Find("Panel/MainMenu");

            mainMenu.Find("Continue").gameObject.SetActive(false);
            mainMenu.Find("NewGame").gameObject.SetActive(false);
            mainMenu.Find("LoadGame").gameObject.SetActive(false);

            Instantiate(_loginForm, mainMenu).transform.SetAsFirstSibling();
            _loginForm = null;

            Instantiate(ResourceManager.Load<GameObject>("ChatPanel", ResourceManager.EXT_PREFAB), Game.World.HUD.Log.transform);
        }

        if (_signed && _fetch)
        {
            _actionTimer -= Time.deltaTime;
            if (_actionTimer <= 0)
            {
                StartCoroutine(TryGetActions());
            }
        }
    }
}
