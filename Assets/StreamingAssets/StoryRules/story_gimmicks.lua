-- Scenario effects belong to the duel instance and are installed before its opening draw.
local rules={}
local countdownReason=0x80

local function aura(code)
    local effect=Effect.GlobalEffect()
    effect:SetType(EFFECT_TYPE_FIELD)
    effect:SetCode(code)
    return effect
end

local function playerRule(player,code,value,condition,target)
    local effect=aura(code)
    effect:SetProperty(EFFECT_FLAG_PLAYER_TARGET)
    effect:SetTargetRange(1,0)
    effect:SetValue(value)
    if condition then effect:SetCondition(condition) end
    if target then effect:SetTarget(target) end
    Duel.RegisterEffect(effect,player)
end

local function monsterRule(player,code,target,value,condition)
    local effect=aura(code)
    effect:SetTargetRange(LOCATION_MZONE,0)
    if target then effect:SetTarget(target) end
    if value~=nil then effect:SetValue(value) end
    if condition then effect:SetCondition(condition) end
    Duel.RegisterEffect(effect,player)
end

local function onEvent(player,event,operation,countLimit)
    local effect=Effect.GlobalEffect()
    effect:SetType(EFFECT_TYPE_FIELD|EFFECT_TYPE_CONTINUOUS)
    effect:SetCode(event)
    effect:SetOperation(operation)
    if countLimit then effect:SetCountLimit(countLimit) end
    Duel.RegisterEffect(effect,player)
end

local function onPhase(player,phase,operation)
    onEvent(player,EVENT_PHASE+phase,function(e)
        operation(e,Duel.GetTurnCount())
    end,1)
end

local function activeField(player,code)
    local card=Duel.GetFieldCard(player,LOCATION_FZONE,0)
    return card and card:IsCode(code) and card:IsFaceup() and not card:IsDisabled()
end

local function countdown(ai,limit)
    onPhase(ai,PHASE_END,function(_,turn)
        if turn>=limit then Duel.Win(ai,countdownReason) end
    end)
end

local function damageEachTurn(ai,human,amount)
    onPhase(ai,PHASE_STANDBY,function()
        Duel.Damage(human,amount,REASON_EFFECT)
    end)
end

local function addAttack(card,amount,reset)
    local effect=Effect.CreateEffect(card)
    effect:SetType(EFFECT_TYPE_SINGLE)
    effect:SetCode(EFFECT_UPDATE_ATTACK)
    effect:SetValue(amount)
    effect:SetReset(RESET_EVENT|RESETS_STANDARD|(reset or 0))
    card:RegisterEffect(effect)
end

function rules.insect_forest(human,ai)
    local parasite
    monsterRule(ai,EFFECT_UPDATE_ATTACK,function(_,c) return c:IsRace(RACE_INSECT) end,
        function(_,c) return math.floor(math.max(c:GetBaseAttack(),0)*0.30)-200 end,
        function() return activeField(ai,87430998) end)
    onPhase(ai,PHASE_STANDBY,function(_,turn)
        if turn~=1 then return end
        local intruder=Duel.CreateToken(ai,27911549)
        if Duel.SendtoDeck(intruder,human,SEQ_DECKSHUFFLE,REASON_EFFECT)>0 and intruder:IsLocation(LOCATION_DECK) then
            Duel.ShuffleDeck(human)
            intruder:ReverseInDeck()
            parasite=intruder
        end
    end)
    onEvent(ai,EVENT_DRAW,function(_,_,eg,ep)
        if ep~=human or not parasite or not eg:IsContains(parasite) then return end
        local drawn=parasite
        parasite=nil
        if Duel.SpecialSummon(drawn,0,human,human,false,false,POS_FACEUP_DEFENSE)>0 then
            Duel.Damage(human,1000,REASON_EFFECT)
            local change=Effect.CreateEffect(drawn)
            change:SetType(EFFECT_TYPE_FIELD)
            change:SetCode(EFFECT_CHANGE_RACE)
            change:SetRange(LOCATION_MZONE)
            change:SetTargetRange(LOCATION_MZONE,0)
            change:SetValue(RACE_INSECT)
            drawn:RegisterEffect(change)
        end
    end)
end

function rules.ocean_ambush(human,ai)
    monsterRule(human,EFFECT_CANNOT_DIRECT_ATTACK,nil,1,function()
        return activeField(ai,22702055) and Duel.IsExistingMatchingCard(
            function(c) return c:IsFaceup() and c:IsRace(RACE_FISH) end,ai,LOCATION_MZONE,0,1,nil)
    end)
end

function rules.undead_renewal(_,ai)
    onEvent(ai,EVENT_DESTROYED,function(e,_,eg)
        for card in aux.Next(eg) do
            local previousAttack=card:GetPreviousAttackOnField()
            if card:IsPreviousControler(ai) and card:IsPreviousLocation(LOCATION_MZONE)
                and card:GetPreviousRaceOnField()&RACE_ZOMBIE~=0 and card:IsLocation(LOCATION_GRAVE)
                and Duel.GetLocationCount(ai,LOCATION_MZONE)>0
                and card:IsCanBeSpecialSummoned(e,0,ai,false,false)
                and Duel.SpecialSummon(card,0,ai,ai,false,false,POS_FACEUP_ATTACK)>0 then
                addAttack(card,math.floor(math.max(previousAttack,0)*0.10))
            end
        end
    end)
end

function rules.darkness_curtain(human,ai)
    monsterRule(human,EFFECT_CANNOT_SELECT_BATTLE_TARGET,nil,
        function(_,card) return card:IsControler(ai) and card:IsFacedown() end)
    local curtain=aura(EFFECT_CANNOT_BE_EFFECT_TARGET)
    curtain:SetTargetRange(LOCATION_SZONE,0)
    curtain:SetTarget(function(_,card) return card:IsFacedown() end)
    curtain:SetValue(function(_,_,player) return player==human end)
    Duel.RegisterEffect(curtain,ai)
end

function rules.machine_immunity(human,ai)
    monsterRule(ai,EFFECT_IMMUNE_EFFECT,function(_,card) return card:IsRace(RACE_MACHINE) end,
        function(_,incoming) return incoming:IsActiveType(TYPE_SPELL) end)
end

function rules.toon_mind_read(human,ai)
    onPhase(ai,PHASE_STANDBY,function()
        local hand=Duel.GetFieldGroup(human,LOCATION_HAND,0)
        if #hand>0 then Duel.ConfirmCards(ai,hand) end
    end)
end

function rules.exodia_draw(_,ai)
    playerRule(ai,EFFECT_DRAW_COUNT,2)
end

function rules.circus_saw(human,ai)
    damageEachTurn(ai,human,300)
end

function rules.slifer_draw(_,ai)
    local function sliferOnField()
        return Duel.IsExistingMatchingCard(function(c) return c:IsCode(10000020) and c:IsFaceup() end,
            ai,LOCATION_MZONE,0,1,nil)
    end
    playerRule(ai,EFFECT_HAND_LIMIT,99,sliferOnField)
    playerRule(ai,EFFECT_DRAW_COUNT,2,sliferOnField)
end

function rules.voodoo_countdown(_,ai)
    countdown(ai,15)
end

function rules.ishtar_foresight(human,ai)
    onEvent(ai,EVENT_PHASE_START+PHASE_DRAW,function()
        if Duel.GetTurnPlayer()==human and Duel.GetFieldGroupCount(human,LOCATION_DECK,0)>0 then
            Duel.ConfirmDecktop(human,3)
        end
    end)
end

function rules.shadow_game(human,ai)
    onEvent(ai,EVENT_DAMAGE,function(_,_,_,ep,amount)
        local count=math.floor(amount/1000)
        if ep==human and count>0 then
            local cards=Duel.GetDecktopGroup(human,count)
            if #cards>0 then Duel.Remove(cards,POS_FACEDOWN_DEFENSE,REASON_EFFECT) end
        end
    end)
end

function rules.orichalcos(human,ai)
    local function forbidden(_,effect)
        return effect:IsHasType(EFFECT_TYPE_ACTIVATE) and effect:GetHandler():IsType(TYPE_FIELD)
    end
    local function sealed() return activeField(ai,48179391) end
    playerRule(human,EFFECT_CANNOT_ACTIVATE,forbidden,sealed)
    playerRule(ai,EFFECT_CANNOT_ACTIVATE,forbidden,sealed)
end

YgoStory={
    rules=rules,
    playerRule=playerRule,
    monsterRule=monsterRule,
    onEvent=onEvent,
    onPhase=onPhase,
    activeField=activeField,
    countdown=countdown,
    damageEachTurn=damageEachTurn,
    addAttack=addAttack,
}

local modules={"story_gx.lua","story_5ds.lua","story_zexal.lua"}
function YgoStoryConfigure(rule,human)
    for _,module in ipairs(modules) do
        if rules[rule] then break end
        if not Duel.LoadScript(module) then error("Could not load story rules: "..module) end
    end
    local install=rules[rule]
    if not install then return false end
    install(human,1-human)
    return true
end
