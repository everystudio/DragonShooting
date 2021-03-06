﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Photon.Pun;
using TMPro;
using Photon.Realtime;

public class BattleManager : MonoBehaviourPunCallbacks
{
    private int myStartTime;        //自分のStart時刻
    private int enemyStartTime;     //相手のStart時刻
    private int battleStartTime;    //バトル開始時刻
    private bool sendMessages = true;   //メッセージ送信フラグ
    //状態定数
    private enum STATUS
    {
        SEND_ST,
        SEND_ST_AND_CONFIRM_ST,
        SEND_CONFIRM_ST,
        BATTLE,
        GAME_OVER,
    }
    private STATUS status = STATUS.SEND_ST;
    //開始位置
    private enum START_POSITION
    {
        LEFT,
        RIGHT,
    }
    private START_POSITION startPosition = START_POSITION.LEFT;
    [SerializeField]
    private GameObject battleInformationPanel;  //バトル情報パネル
    [SerializeField]
    private GameObject loadingPanel;  //ローディングパネル
    [SerializeField]
    private Slider hpSlider;            //HPスライダー
    private GameObject myCharacter = null;      //自キャラ
    private bool win = false;       //バトル勝利判定
    [SerializeField]
    private SaveDataManager saveDataManager;
    [SerializeField]
    private BGMManager bgmManager;
    [SerializeField]
    private List<BulletController> allAbilities;
    void Start()
    {
        loadingPanel.SetActive(true);                   //ローディングパネルを表示
        battleInformationPanel.SetActive(false);        //バトル情報パネルを非表示
        myStartTime = PhotonNetwork.ServerTimestamp;    //スタート時刻を保存
        Debug.Log(myStartTime);
    }
    void Update()
    {
        //メッセージ送信フラグが真のときのみメッセージ送信処理を行う
        if (sendMessages)
        {
            sendMessages = false;
            StartCoroutine(SendMessages());
        }
        //確認のみ送信の状態のとき
        if (status == STATUS.SEND_CONFIRM_ST)
        {
            //バトル開始時刻になったら
            if (battleStartTime <= PhotonNetwork.ServerTimestamp)
            {
                status = STATUS.BATTLE;
                //オブジェクトの生成
                Vector2 position = new Vector2(0f, 0f);
                if (startPosition == START_POSITION.LEFT)
                {
                    position = new Vector2(-4f, 0f);
                }
                else if (startPosition == START_POSITION.RIGHT)
                {
                    position = new Vector2(4f, 0f);
                }
                SetObject(position, saveDataManager.GetPlayerData().GetBattleCharacter());
                //カウントダウン
                Debug.Log("BattleCountDownStart");
                StartCoroutine(BattleCountDown());
            }
        }
    }
    //メッセージ送信処理
    IEnumerator SendMessages()
    {
        //スタート時刻送信
        if(status == STATUS.SEND_ST || status == STATUS.SEND_ST_AND_CONFIRM_ST)
        {
            photonView.RPC(nameof(GetStartTime), RpcTarget.Others, myStartTime);
        }
        //確認のための送信
        if(status == STATUS.SEND_ST_AND_CONFIRM_ST || status == STATUS.SEND_CONFIRM_ST)
        {
            photonView.RPC(nameof(ConfirmGetStartTime), RpcTarget.Others);
        }
        yield return new WaitForSeconds(1);     //メッセージ送信待機時間
        sendMessages = true;                    //送信フラグ真
    }
    //ネットワークオブジェクトの作成
    public void SetObject(Vector2 position, string objectName)
    {
        myCharacter = PhotonNetwork.Instantiate(objectName, position, Quaternion.identity);
        myCharacter.GetComponent<PlayerController>().SetStartScale();
        //アイコンの色変更
        Color myIconColor = myCharacter.transform.Find("Icon").GetComponent<SpriteRenderer>().color;
        myIconColor.r = 0f;
        myIconColor.g = 1f;
        myCharacter.transform.Find("Icon").GetComponent<SpriteRenderer>().color = myIconColor;
        myCharacter.transform.Find("Icon").transform.Find("PlayerNameText").GetComponent<TextMesh>().color = myIconColor;
        //名前の取り付け
        PlayerDataAsset playerDataAsset = saveDataManager.GetPlayerDataAsset();
        myCharacter.GetComponent<PlayerController>().SetPlayerName(playerDataAsset.GetPlayersName());
        myCharacter.GetComponent<PlayerController>().SetHPSlider(hpSlider);     //HPスライダーの取付
        //アビリティ取り付け
        string characterData = playerDataAsset.GetCharacterData();
        List<string> characterDataList = new List<string>();
        if (characterData.Contains("/"))
        {
            characterDataList.AddRange(characterData.Split('/'));
        }
        else
        {
            characterDataList.Add(characterData);
        }
        foreach(string data in characterDataList)
        {
            List<string> dataList = new List<string>();
            dataList.AddRange(data.Split(':'));
            if(dataList[0] == objectName)
            {
                foreach(BulletController ability in allAbilities)
                {
                    for(int i = 1; i < 3; i++)
                    {
                        if (ability.name == dataList[i])
                        {
                            myCharacter.GetComponent<PlayerController>().SetAbility(ability, i);
                        }
                    }
                }
            }
        }
        //効果音変更
        myCharacter.GetComponent<PlayerController>().SetSEVolume(playerDataAsset.GetSEVolume());
        //右側スタートなら向きを変更
        if (startPosition == START_POSITION.RIGHT)
        {
            myCharacter.GetComponent<PlayerController>().SwichPlayersDirection();
        }
    }
    //相手のシーン開始サーバー時刻の受け取り
    [PunRPC]
    private void GetStartTime(int startTime)
    {
        if (status == STATUS.SEND_ST)
        {
            status = STATUS.SEND_ST_AND_CONFIRM_ST;
            enemyStartTime = startTime;
            Debug.Log(enemyStartTime);
        }
    }
    //シーン開始サーバー時刻の受け取り確認
    [PunRPC]
    private void ConfirmGetStartTime()
    {
        if (status == STATUS.SEND_ST_AND_CONFIRM_ST)
        {
            status = STATUS.SEND_CONFIRM_ST;
            battleStartTime = Mathf.Max(myStartTime, enemyStartTime) + 3000;
            if (battleStartTime == myStartTime + 3000)
            {
                startPosition = START_POSITION.LEFT;
            }
            else
            {
                startPosition = START_POSITION.RIGHT;
            }
        }
    }
    //カウントダウン処理
    IEnumerator BattleCountDown()
    {
        loadingPanel.SetActive(false);
        battleInformationPanel.SetActive(true);
        PlayRandomBGM();    //BGM開始
        for (int i = 0; i < 3; i++)
        {
            int count = 3 - i;
            Debug.Log(count.ToString());
            SetCountDownTMP(count.ToString());
            yield return new WaitForSeconds(1);
        }
        Debug.Log("BattleStart!");
        SetCountDownTMP("GO!");
        myCharacter.GetComponent<PlayerController>().SwitchWait(false);
        yield return new WaitForSeconds(1);
        battleInformationPanel.SetActive(false);
    }
    //カウントダウン表示用テキスト(TMP)のテキスト変更
    private void SetCountDownTMP(string text)
    {
        battleInformationPanel.transform.Find("CountDownTMP").GetComponent<TextMeshProUGUI>().text = text;
    }
    //ゲームオーバー処理
    IEnumerator GameOver()
    {
        yield return new WaitForSeconds(2);
        battleInformationPanel.SetActive(true);
        SetCountDownTMP("GAME SET");
        yield return new WaitForSeconds(2);
        if (win)
        {
            SetCountDownTMP("YOU WIN!");
            PlayerData playerData = saveDataManager.GetPlayerData();
            playerData.IncrementWinCount();
            saveDataManager.SetPlayerData(playerData);
            saveDataManager.Save();

        }
        else
        {
            SetCountDownTMP("YOU LOSE...");
        }
        yield return new WaitForSeconds(2);
        PhotonNetwork.LeaveRoom();  //ルーム退出
        SceneManager.LoadScene("RoomLobby", LoadSceneMode.Single);  //ロビーシーンへ移動
    }
    //他プレイヤー退出時コールバック
    public override void OnPlayerLeftRoom(Player player)
    {
        if(status == STATUS.GAME_OVER)
        {
            return;
        }
        Debug.Log(player.NickName + "が退出しました");
        win = true;
        StartCoroutine(GameOver());
    }
    public void GameOverProcess(bool _win)
    {
        status = STATUS.GAME_OVER;
        win = _win;
        if (win)
        {
            myCharacter.GetComponent<PlayerController>().SwitchWait(true);
        }
        StartCoroutine(GameOver());
    }
    public void PlayRandomBGM()
    {
        PlayerDataAsset playerDataAsset = saveDataManager.GetPlayerDataAsset();
        bgmManager.SetBGMVolume(playerDataAsset.GetBGMVolume());
        bgmManager.RandomPlay();
    }
    public BulletController GetAbility(int id)
    {
        foreach(BulletController projectile in allAbilities)
        {
            if(projectile.GetID() == id)
            {
                return projectile;
            }
        }
        return null;
    }
}