local story=YgoStory
local rules=story.rules

local function find(player,location,code)
    return Duel.GetMatchingGroup(Card.IsCode,player,location,0,nil,code):GetFirst()
end

local function faceupMonster(player,code)
    return Duel.GetMatchingGroup(function(card)
        return card:IsFaceup() and card:IsCode(code)
    end,player,LOCATION_MZONE,0,nil):GetFirst()
end

local function groupWithCodes(player,codes,from)
    local group=Group.CreateGroup()
    for _,code in ipairs(codes) do
        local card=from(player,code)
        if not card then return nil end
        group:AddCard(card)
    end
    return group
end

local function addExtra(player,code)
    if not find(player,LOCATION_EXTRA,code) then
        Duel.SendtoDeck(Duel.CreateToken(player,code),player,SEQ_DECKTOP,REASON_RULE)
    end
end

local function addOverlay(ai,code,codes)
    story.onEvent(ai,EVENT_STARTUP,function()
        local target=faceupMonster(ai,code)
        if not target then return end
        local group=Group.CreateGroup()
        for _,material in ipairs(codes) do group:AddCard(Duel.CreateToken(ai,material)) end
        Duel.Overlay(target,group)
    end)
end

local function actionField(human)
    story.onEvent(human,EVENT_STARTUP,function()
        local card=Duel.CreateToken(human,511090301)
        Duel.MoveToField(card,human,human,LOCATION_FZONE,POS_FACEUP,true)
    end)
end

local function onRivalMain(ai,operation)
    story.onEvent(ai,EVENT_PHASE_START+PHASE_MAIN1,function()
        if Duel.IsTurnPlayer(ai) then operation() end
    end)
end

function rules.action_opening(human)
    actionField(human)
end

function rules.showmanship(_,ai)
    story.onEvent(ai,EVENT_ATTACK_ANNOUNCE,function()
        local attacker=Duel.GetAttacker()
        if attacker and attacker:IsControler(ai) and attacker:IsSetCard(SET_ABYSS_ACTOR) then
            story.addAttack(attacker,400)
        end
    end)
end

function rules.frightfur_circus(human,ai)
    story.onEvent(ai,EVENT_DESTROYED,function(_,_,cards)
        local destroyed=cards:FilterCount(function(card)
            return card:IsPreviousLocation(LOCATION_MZONE) and card:IsPreviousControler(ai) and card:IsSetCard(SET_FRIGHTFUR)
        end,nil)
        if destroyed>0 then Duel.Damage(human,destroyed*500,REASON_EFFECT) end
    end)
end

local function synchro(ai,code,parts)
    local summon=find(ai,LOCATION_EXTRA,code)
    local materials=groupWithCodes(ai,parts,faceupMonster)
    if summon and materials and summon:IsSynchroSummonable(nil,materials,#parts,#parts) then
        Duel.SynchroSummon(ai,summon,nil,materials,#parts,#parts)
    end
end

local function xyz(ai,code,parts)
    local summon=find(ai,LOCATION_EXTRA,code)
    local materials=groupWithCodes(ai,parts,faceupMonster)
    if summon and materials and summon:IsXyzSummonable(nil,materials,#parts,#parts) then
        Duel.XyzSummon(ai,summon,nil,materials,#parts,#parts)
    end
end

local function dddOpening(ai,cards)
    story.onEvent(ai,EVENT_STARTUP,function()
        for _,name in ipairs({cards.fusion,cards.synchro,cards.xyz}) do addExtra(ai,name) end
        local graveSetup=Group.CreateGroup()
        for _,material in ipairs(cards.fusionParts) do
            graveSetup:AddCard(find(ai,LOCATION_DECK,material) or Duel.CreateToken(ai,material))
        end
        Duel.SendtoGrave(graveSetup,REASON_RULE)
    end)
    onRivalMain(ai,function()
        local fusion=find(ai,LOCATION_EXTRA,cards.fusion)
        local materials=groupWithCodes(ai,cards.fusionParts,function(player,code)
            local card=find(player,LOCATION_GRAVE,code)
            return card and card:IsAbleToRemove() and card
        end)
        if fusion and materials and fusion:CheckFusionMaterial(materials,nil,ai) and
            Duel.GetLocationCountFromEx(ai,ai,nil,fusion)>0 then
            fusion:SetMaterial(materials)
            if Duel.Remove(materials,POS_FACEUP,REASON_EFFECT|REASON_FUSION|REASON_MATERIAL)==#materials and
                Duel.SpecialSummon(fusion,SUMMON_TYPE_FUSION,ai,ai,false,false,POS_FACEUP_ATTACK)>0 then
                fusion:CompleteProcedure()
            end
        end
        synchro(ai,cards.synchro,cards.synchroParts)
        xyz(ai,cards.xyz,cards.xyzParts)
    end)
end

function rules.action_ddd(human,ai)
    actionField(human)
    dddOpening(ai,{
        fusion=74583607,fusionParts={45206713,72291412},
        synchro=987311,synchroParts={48210156,12822541},
        xyz=3758046,xyzParts={19808608,46796664},
    })
end

function rules.raidraptor_resistance(_,ai)
    addOverlay(ai,73887236,{53251824,5929801,96345188})
    story.monsterRule(ai,EFFECT_UPDATE_ATTACK,function(_,card)
        return card:IsSetCard(SET_RAIDRAPTOR)
    end,1500,function() return Duel.GetLP(ai)<=4000 end)
end

function rules.action_speedroid(human,ai)
    actionField(human)
    story.onEvent(ai,EVENT_STARTUP,function() addExtra(ai,82044279) end)
    onRivalMain(ai,function() synchro(ai,82044279,{16725505,81275020,53932291}) end)
end

function rules.action_dark_rebellion(human,ai)
    actionField(human)
    addOverlay(ai,16195942,{96345188,83236601})
end

function rules.lunalight_dance(human,ai)
    local function dancer(_,card) return card:IsSetCard(SET_LUNALIGHT) end
    story.monsterRule(ai,EFFECT_EXTRA_ATTACK,dancer,1)
    story.monsterRule(ai,EFFECT_CANNOT_BE_EFFECT_TARGET,dancer,function(_,_,source)
        return source==human
    end)
end

function rules.windwitch_barrier(_,ai)
    local function barrier() return faceupMonster(ai,14577226)~=nil end
    story.playerRule(ai,EFFECT_CHANGE_DAMAGE,function(_,_,damage,reason)
        if reason&REASON_EFFECT~=0 then return math.floor(damage/2) end
        return damage
    end,barrier)
end

function rules.action_king(human)
    actionField(human)
end

function rules.predator_counters(human,ai)
    actionField(human)
    local function poison(cards)
        local source=faceupMonster(ai,69946549)
        if not source or source:IsDisabled() then return end
        for card in aux.Next(cards) do
            if card:IsControler(human) and card:IsLocation(LOCATION_MZONE) and card:IsFaceup() then
                card:AddCounter(COUNTER_PREDATOR,1)
            end
        end
    end
    for _,event in ipairs({EVENT_SUMMON_SUCCESS,EVENT_FLIP_SUMMON_SUCCESS,EVENT_SPSUMMON_SUCCESS}) do
        story.onEvent(ai,event,function(_,_,cards) poison(cards) end)
    end
    story.onPhase(ai,PHASE_STANDBY,function()
        if Duel.IsTurnPlayer(human) then poison(Duel.GetFieldGroup(human,LOCATION_MZONE,0)) end
    end)
end

function rules.zarc_barrier(human,ai)
    story.playerRule(human,EFFECT_CANNOT_SPECIAL_SUMMON,1,
        function()
            local zarc=faceupMonster(ai,13331639)
            return zarc and not zarc:IsDisabled()
        end,function(_,card) return card:IsLocation(LOCATION_EXTRA) end)
end

function rules.ddd_final_trial(_,ai)
    dddOpening(ai,{
        fusion=16006416,fusionParts={47198668,45206713},
        synchro=44852429,synchroParts={72181263,12822541},
        xyz=18897163,xyzParts={16006416,44852429},
    })
end

function rules.four_dragons(_,ai)
    local code={42160203,70771599,43387895}
    local summonType={SUMMON_TYPE_XYZ,SUMMON_TYPE_SYNCHRO,SUMMON_TYPE_FUSION}
    local wave=1
    local current=96733134
    story.onEvent(ai,EVENT_STARTUP,function()
        for _,dragon in ipairs(code) do addExtra(ai,dragon) end
    end)
    story.onPhase(ai,PHASE_STANDBY,function(e)
        if not Duel.IsTurnPlayer(ai) or faceupMonster(ai,current) then return end
        local dragon=find(ai,LOCATION_EXTRA,code[wave])
        if not dragon or not dragon:IsCanBeSpecialSummoned(e,summonType[wave],ai,true,true) then return end
        if Duel.SpecialSummon(dragon,summonType[wave],ai,ai,true,true,POS_FACEUP_ATTACK)>0 then
            dragon:CompleteProcedure()
            if wave==1 then
                Duel.Overlay(dragon,Group.FromCards(Duel.CreateToken(ai,69610326),Duel.CreateToken(ai,41908872)))
            end
            current=code[wave]
            wave=wave+1
            if wave>#code then e:Reset() end
        end
    end)
end

function rules.smile_universe(_,ai)
    story.onEvent(ai,EVENT_STARTUP,function()
        for _,code in ipairs({26270847,47075569}) do
            local card=find(ai,LOCATION_DECK,code) or Duel.CreateToken(ai,code)
            Duel.SendtoExtraP(card,ai,REASON_RULE)
        end
        local spell=find(ai,LOCATION_DECK,35259350) or Duel.CreateToken(ai,35259350)
        Duel.DisableShuffleCheck()
        Duel.SendtoDeck(spell,ai,SEQ_DECKTOP,REASON_RULE)
    end)
end
