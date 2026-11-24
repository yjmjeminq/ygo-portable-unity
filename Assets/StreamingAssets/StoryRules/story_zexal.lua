local story=YgoStory
local rules=story.rules
local chaosCounter=0x0013
local numerounia=15862758

local function find(player,location,code)
    return Duel.GetMatchingGroup(Card.IsCode,player,location,0,nil,code):GetFirst()
end

local function faceupMonster(player,code)
    return Duel.GetMatchingGroup(function(card)
        return card:IsFaceup() and card:IsCode(code)
    end,player,LOCATION_MZONE,0,nil):GetFirst()
end

local function ensureExtra(player,code)
    if not find(player,LOCATION_EXTRA,code) then
        Duel.SendtoDeck(Duel.CreateToken(player,code),player,SEQ_DECKTOP,REASON_RULE)
    end
end

local function addMaterials(ai,code,materialCodes)
    story.onEvent(ai,EVENT_STARTUP,function()
        local xyz=faceupMonster(ai,code)
        if not xyz then return end
        local materials=Group.CreateGroup()
        for _,material in ipairs(materialCodes) do
            materials:AddCard(Duel.CreateToken(ai,material))
        end
        Duel.Overlay(xyz,materials)
    end)
end

local function openWithXyz(ai,code,parts)
    story.onEvent(ai,EVENT_STARTUP,function()
        ensureExtra(ai,code)
    end)
    local attempted=false
    story.onEvent(ai,EVENT_PHASE_START+PHASE_MAIN1,function()
        if attempted or not Duel.IsTurnPlayer(ai) then return end
        local materials=Group.CreateGroup()
        for _,part in ipairs(parts) do
            local card=faceupMonster(ai,part)
            if not card then return end
            materials:AddCard(card)
        end
        local xyz=find(ai,LOCATION_EXTRA,code)
        if not xyz or not xyz:IsXyzSummonable(nil,materials,#parts,#parts) then return end
        attempted=true
        Duel.XyzSummon(ai,xyz,nil,materials,#parts,#parts)
    end)
end

function rules.numbers_protection(_,ai)
    addMaterials(ai,31801517,{93717133,35950025})
    story.monsterRule(ai,EFFECT_INDESTRUCTABLE_BATTLE,function(_,card)
        return card:IsSetCard(SET_NUMBER)
    end,function(_,attacker)
        return not attacker:IsSetCard(SET_NUMBER)
    end)
end

function rules.chronomaly_colosseum(_,ai)
    openWithXyz(ai,39139935,{87430304,5291803})
    story.onEvent(ai,EVENT_SPSUMMON_SUCCESS,function(_,_,cards)
        for player=0,1 do
            local count=cards:FilterCount(function(card)
                return card:IsControler(player) and card:IsSummonType(SUMMON_TYPE_XYZ)
            end,nil)
            if count>0 then Duel.Damage(1-player,500*count,REASON_EFFECT) end
        end
    end)
end

function rules.gimmick_puppet_show(_,ai)
    addMaterials(ai,88120966,{92418590,39806198})
end

function rules.heartland_cannon(human,ai)
    story.damageEachTurn(ai,human,500)
end

function rules.barian_hand(human,ai)
    local seal=Effect.GlobalEffect()
    seal:SetType(EFFECT_TYPE_FIELD)
    seal:SetCode(EFFECT_DISABLE_FIELD)
    seal:SetValue(1<<(human*16+2))
    Duel.RegisterEffect(seal,ai)
end

function rules.star_seraph_xyz(_,ai)
    openWithXyz(ai,49678559,{38331564,91110378,70668285})
end

function rules.barian_chaos(_,ai)
    local base,upgraded=2061963,49456901
    story.onEvent(ai,EVENT_STARTUP,function()
        local ruler=faceupMonster(ai,base)
        if ruler then ruler:EnableCounterPermit(chaosCounter) end
        ensureExtra(ai,upgraded)
    end)
    story.onPhase(ai,PHASE_STANDBY,function(e)
        if not Duel.IsTurnPlayer(ai) then return end
        local ruler=faceupMonster(ai,base)
        if not ruler or ruler:IsDisabled() then return end
        if ruler:GetCounter(chaosCounter)<2 then ruler:AddCounter(chaosCounter,1) end
        if ruler:GetCounter(chaosCounter)<2 then return end
        local xyz=find(ai,LOCATION_EXTRA,upgraded)
        if not xyz or not xyz:IsCanBeSpecialSummoned(e,SUMMON_TYPE_XYZ,ai,false,false)
            or Duel.GetLocationCountFromEx(ai,ai,ruler,xyz)<=0 then return end
        e:Reset()
        local materials=Group.FromCards(ruler)
        xyz:SetMaterial(materials)
        Duel.Overlay(xyz,materials)
        if Duel.SpecialSummon(xyz,SUMMON_TYPE_XYZ,ai,ai,false,false,POS_FACEUP_ATTACK)>0 then
            xyz:CompleteProcedure()
        end
    end)
end

function rules.boxing_ring(_,ai)
    addMaterials(ai,59627393,{20721928,26082117})
    for player=0,1 do
        story.playerRule(player,EFFECT_CANNOT_MSET,1)
        story.playerRule(player,EFFECT_FORCE_SPSUMMON_POSITION,POS_FACEUP_ATTACK)
        story.monsterRule(player,EFFECT_CANNOT_CHANGE_POSITION,nil,1)
        story.monsterRule(player,EFFECT_CANNOT_CHANGE_POS_E,nil,POS_DEFENSE|POS_FACEDOWN_ATTACK)
        story.playerRule(player,EFFECT_CHANGE_DAMAGE,function(_,_,amount,reason)
            return reason&REASON_BATTLE~=0 and amount*2 or amount
        end)
    end
end

function rules.tachyon_rewind(_,ai)
    addMaterials(ai,88177324,{93717133,35950025})
    local battleDamage
    story.onEvent(ai,EVENT_PHASE_START+PHASE_BATTLE_START,function()
        battleDamage={[0]=0,0}
    end)
    story.onEvent(ai,EVENT_DAMAGE,function(_,_,_,player,damage,_,reason)
        if battleDamage and reason&REASON_BATTLE~=0 then
            battleDamage[player]=battleDamage[player]+damage
        end
    end)
    story.onPhase(ai,PHASE_BATTLE,function()
        local lost=battleDamage
        battleDamage=nil
        local tachyon=faceupMonster(ai,88177324)
        if not lost or not tachyon or tachyon:IsDisabled() then return end
        for player=0,1 do
            if lost[player]>0 and Duel.GetLP(player)>0 then Duel.SetLP(player,Duel.GetLP(player)+lost[player]) end
        end
    end)
end

function rules.dark_mist(_,ai)
    addMaterials(ai,55727845,{59708927,59575539,94203886})
end

function rules.ark_knight_material(_,ai)
    addMaterials(ai,48739166,{20721928,26082117})
end

function rules.gorgonic_ghost(human,ai)
    addMaterials(ai,84401683,{37984162,64379261})
    story.onPhase(ai,PHASE_STANDBY,function()
        local count=Duel.GetFieldGroupCount(human,LOCATION_DECK,0)
        if count>0 then Duel.DiscardDeck(human,math.min(3,count),REASON_EFFECT) end
    end)
end

function rules.numeronius(human,ai)
    local function boss(_,card) return card:IsCode(numerounia) end
    local function locked() return not YgoStoryShiningUsed end
    story.monsterRule(ai,EFFECT_SET_ATTACK_FINAL,boss,100000)
    story.monsterRule(ai,EFFECT_SET_DEFENSE_FINAL,boss,100000)
    for _,restriction in ipairs({EFFECT_INDESTRUCTABLE_BATTLE,EFFECT_CANNOT_ATTACK,
        EFFECT_UNRELEASABLE_SUM,EFFECT_UNRELEASABLE_NONSUM,EFFECT_CANNOT_CHANGE_CONTROL,
        EFFECT_CANNOT_BE_FUSION_MATERIAL,EFFECT_CANNOT_BE_MATERIAL}) do
        story.monsterRule(ai,restriction,boss,1,locked)
    end
    story.monsterRule(ai,EFFECT_IMMUNE_EFFECT,boss,function(_,incoming)
        return incoming:GetHandlerPlayer()==human
    end,locked)

    story.onEvent(ai,EVENT_STARTUP,function()
        if not find(human,LOCATION_DECK,12580477) then
            Duel.DisableShuffleCheck()
            Duel.SendtoDeck(Duel.CreateToken(human,12580477),human,SEQ_DECKBOTTOM,REASON_RULE)
        end
    end)
    story.onPhase(ai,PHASE_STANDBY,function(e)
        if not Duel.IsTurnPlayer(human) then return end
        e:Reset()
        if Duel.GetLP(human)>2000 then Duel.SetLP(human,2000) end
    end)
end

function rules.barian_emperors(_,ai)
    addMaterials(ai,12744567,{48739166,95929069})
end

-- One original Number for each number from 1 through 100.
local numbers={
    10666000,32453837,4997565,4019153,90126061,9161357,82308875,47387961,1992816,11411223,
    80117527,19333131,69058960,21313376,88120966,54719828,69610924,23649496,55067058,47805931,
    57707471,73445448,66547759,93713837,64554883,39622156,8387138,53701457,54191698,81330115,
    95442074,65676461,39139935,32003338,90162951,50260683,37279508,63767246,84013237,75433814,
    90590303,10389142,56051086,80764541,29208536,2978414,31320433,1426714,16259549,51735257,
    56292140,7194917,23998625,54366836,46871387,55935416,53244294,93108839,82697249,66011101,
    29669359,31801517,89642993,39972129,3790062,76067258,35772782,23085002,2407234,80796456,
    59479050,75253697,36076683,16037007,71166481,92015800,62541668,29085954,71921856,93568288,
    49032236,31437713,48928529,26556950,42421606,63504681,89516305,48995978,95474755,8165596,
    84417082,97403510,23187256,62070231,58820923,55727845,28400508,55470553,51543904,57314798,
}

function rules.hundred_numbers(_,ai)
    addMaterials(ai,84013237,{20721928,26082117})
    story.onEvent(ai,EVENT_STARTUP,function()
        local pool={}
        for _,code in ipairs(numbers) do
            if not find(ai,LOCATION_EXTRA,code) and not find(ai,LOCATION_MZONE,code) then
                pool[#pool+1]=code
            end
        end
        for _=1,math.min(3,#pool) do
            local drawn=Duel.GetRandomNumber(1,#pool)
            ensureExtra(ai,table.remove(pool,drawn))
        end
    end)
end

function rules.hope_roots(_,ai)
    local root=84124261
    local function charge()
        local xyz=faceupMonster(ai,root)
        if xyz and not xyz:IsDisabled() and xyz:GetOverlayCount()==0 then
            Duel.Overlay(xyz,Duel.CreateToken(ai,2830693))
        end
    end
    story.onEvent(ai,EVENT_STARTUP,charge)
    story.onEvent(ai,EVENT_ATTACK_ANNOUNCE,charge)
end

function rules.uturpic_finale(_,ai)
    addMaterials(ai,65305468,{12014404,91499077})
end
