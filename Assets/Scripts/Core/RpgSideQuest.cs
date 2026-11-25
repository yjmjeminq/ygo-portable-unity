using System;
using System.Linq;
using UnityEngine;

namespace YGOPortable.Core
{
    internal enum RpgQuestState { Available, Active, Ready, Complete }

    [Serializable]
    internal sealed class RpgQuestRecord
    {
        public string id;
        public RpgQuestState state;
    }

    internal sealed class RpgSideQuest
    {
        internal readonly string Id, Region, Title, Greeting, Item, Objective, Receiver;
        internal readonly int Giver, Reward;
        internal readonly Vector2 Point;

        private RpgSideQuest(string id, string region, int giver, string title, string greeting,
            string item, string objective, int reward, Vector2 point = default, string receiver = null)
        {
            (Id, Region, Giver, Title, Greeting, Item, Objective, Reward, Point, Receiver) =
                (id, region, giver, title, greeting, item, objective, reward, point, receiver);
        }

        internal static readonly RpgSideQuest[] All =
        {
            new("garage-tools", "spot_kai_garage", 10, "정비소의 잃어버린 공구",
                "렌치가 없어져서 정비를 못 끝냈어. 오른쪽 작업대 밑을 한번 봐줄래?",
                "공구 가방", "정비소 오른쪽 작업대 아래에서 공구 가방을 찾기", 180, new(.83f, .18f)),
            new("hideout-parts", "spot_satellite_hideout", 33, "차고로 보낼 부품",
                "이 부품 상자를 카이의 정비소에 있는 크로우에게 전해주겠니? 전해준 뒤 내게도 알려다오.",
                "부품 상자", "카이의 정비소에서 크로우 호건에게 부품 상자 전하기", 300, receiver: "garage-tools"),
            new("prison-intercom", "spot_facility_prison", 34, "끊어진 연락선",
                "왼쪽 통로의 통신기에 전원이 들어오는지 보고 와. 수리반에 전달할 기록이 필요해.",
                "통신기 점검 기록", "수용소 왼쪽 통로의 통신기를 조사하기", 240, new(.13f, .19f)),
            new("poppo-recorder", "spot_poppo_time", 9, "잃어버린 녹음기",
                "취재 녹음기를 놓고 왔나 봐. 오른쪽 테이블 쪽을 같이 찾아줄래?",
                "녹음기", "폿포 타임 오른쪽 테이블 근처에서 녹음기를 찾기", 280, new(.82f, .19f)),
            new("stadium-schedule", "spot_fortune_stadium", 208, "경기장 취재 일정",
                "취재 일정이 바뀌었어요. 폿포 타임의 칼리 기자님께 새 일정표를 전해주시겠어요?",
                "변경된 일정표", "폿포 타임에서 칼리 나기사에게 일정표 전하기", 380, receiver: "poppo-recorder"),
            new("moment-signal", "spot_old_moment", 24, "폐허에 남은 신호",
                "왼편에서 짧은 신호가 들려. 남아 있는 장치를 찾아서 어떤 신호인지 확인해 줄래?",
                "신호 기록", "구 모멘트 폐허 왼편의 장치를 조사하기", 340, new(.14f, .20f)),
            new("bridge-reflector", "spot_daedalus_bridge", 35, "다리의 안전 표지",
                "난간에 달아둔 반사판 하나가 떨어졌어. 다리 오른쪽을 찾아봐 줘.",
                "떨어진 반사판", "다이달로스 브리지 오른쪽에서 반사판 회수하기", 380, new(.84f, .19f)),
            new("wrgp-track", "spot_wrgp_circuit", 16, "개막 전 노면 점검",
                "출발선 왼쪽의 센서부터 확인해. 레이스에서 노면 정보가 틀리는 건 용납할 수 없어.",
                "센서 점검 기록", "WRGP 서킷 출발선 왼편의 센서를 조사하기", 430, new(.14f, .17f)),
            new("cradle-memory", "spot_ark_cradle", 3204, "아크 크레이들의 기억",
                "오른쪽 끝에 보조 단말기가 하나 남아 있어. 전원이 끊기기 전에 기록을 가져와 줘.",
                "단말기 기록", "아크 크레이들 오른편의 보조 단말기를 조사하기", 500, new(.84f, .19f)),
        };

        internal static RpgSideQuest Find(string id) => All.FirstOrDefault(quest => quest.Id == id);
    }
}
