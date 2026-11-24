local story=YgoStory
local rules=story.rules
local perfectron=13203964

local function find(player,location,code)
    return Duel.GetMatchingGroup(Card.IsCode,player,location,0,nil,code):GetFirst()
end

local function live(player,code)
    return Duel.GetMatchingGroup(function(card)
        return card:IsFaceup() and not card:IsDisabled() and card:IsOriginalCode(code)
    end,player,LOCATION_MZONE,0,nil):GetFirst()
end

local function placeOnDeck(ai,code)
    local card=find(ai,LOCATION_DECK,code) or Duel.CreateToken(ai,code)
    Duel.DisableShuffleCheck()
    Duel.SendtoDeck(card,ai,SEQ_DECKTOP,REASON_RULE)
end

local function addExtra(ai,code)
    if not find(ai,LOCATION_EXTRA,code) then
        Duel.SendtoDeck(Duel.CreateToken(ai,code),ai,SEQ_DECKTOP,REASON_RULE)
    end
end

local function singleRule(card,code,value,reset)
    local effect=Effect.CreateEffect(card)
    effect:SetType(EFFECT_TYPE_SINGLE)
    effect:SetCode(code)
    effect:SetValue(value)
    effect:SetReset(RESET_EVENT|RESETS_STANDARD|(reset or 0))
    card:RegisterEffect(effect)
end

function rules.hanoi_backdoor(_,ai)
    story.onEvent(ai,EVENT_STARTUP,function()
        placeOnDeck(ai,43839002)
    end)
end

function rules.gouki_showdown(_,ai)
    local function protected(_,_,damage,reason)
        if reason&REASON_BATTLE==0 then return damage end
        local fighter,rival=Duel.GetBattleMonster(ai)
        if not fighter or not rival or not fighter:IsSetCard(SET_GOUKI) then return damage end
        if fighter==Duel.GetAttacker() and rival:IsDefensePos() then return damage end
        local fighterPower=fighter:IsDefensePos() and fighter:GetDefense() or fighter:GetAttack()
        local rivalPower=rival:IsDefensePos() and rival:GetDefense() or rival:GetAttack()
        return rivalPower>=fighterPower and 0 or damage
    end
    for player=0,1 do story.playerRule(player,EFFECT_CHANGE_DAMAGE,protected) end
    local revived={}
    story.onEvent(ai,EVENT_DESTROYED,function(e,_,cards)
        for card in aux.Next(cards) do
            if not revived[card] and card:IsPreviousLocation(LOCATION_MZONE) and card:IsPreviousControler(ai)
                and card:IsSetCard(SET_GOUKI) and card:IsLocation(LOCATION_GRAVE)
                and Duel.GetLocationCount(ai,LOCATION_MZONE)>0 and card:IsCanBeSpecialSummoned(e,0,ai,false,false) then
                if Duel.SpecialSummon(card,0,ai,ai,false,false,POS_FACEUP_ATTACK)>0 then
                    revived[card]=true
                    story.addAttack(card,500)
                end
            end
        end
    end)
end

function rules.trickstar_poison(human,ai)
    story.onEvent(ai,EVENT_DRAW,function(_,_,_,player,count)
        if player==human and count>0 and Duel.GetTurnCount()>0 and story.activeField(ai,35371948) then
            Duel.Damage(human,count*200,REASON_EFFECT)
        end
    end)
    story.onEvent(ai,EVENT_CHAINING,function(_,_,_,player)
        if player==human and story.activeField(ai,35371948) then Duel.Damage(human,200,REASON_EFFECT) end
    end)
end

function rules.sunavalon_fortress(_,ai)
    story.onEvent(ai,EVENT_DAMAGE,function(_,_,_,player,damage)
        if player==ai and Duel.GetLP(ai)>0 and live(ai,92770064) then
            Duel.Recover(ai,damage,REASON_EFFECT)
        end
    end)
end

function rules.lightning_link(human,ai)
    story.monsterRule(ai,EFFECT_CANNOT_BE_EFFECT_TARGET,function(_,card)
        local guard=live(ai,21887175)
        return guard and guard:GetLinkedGroup():IsContains(card)
    end,function(_,_,source) return source==human end)
end

function rules.salamangreat_reincarnation(_,ai)
    local wolf=87871125
    story.onEvent(ai,EVENT_STARTUP,function() addExtra(ai,wolf) end)
    local assembled=false
    story.onEvent(ai,EVENT_PHASE_START+PHASE_MAIN1,function()
        if assembled or not Duel.IsTurnPlayer(ai) or not story.activeField(ai,1295111) then return end
        local starter,copy=live(ai,wolf),find(ai,LOCATION_EXTRA,wolf)
        if not starter or not copy or not copy:IsLinkSummonable(starter,starter,1,1) then return end
        assembled=true
        Duel.LinkSummon(ai,copy,starter,starter,1,1)
    end)
    story.onEvent(ai,EVENT_SPSUMMON_SUCCESS,function(_,_,cards)
        for card in aux.Next(cards) do
            if card:IsControler(ai) and card:IsSetCard(SET_SALAMANGREAT) and card:IsLinkSummoned() and
                card:GetMaterial():IsExists(Card.IsCode,1,nil,card:GetCode()) then
                singleRule(card,EFFECT_SET_ATTACK_FINAL,card:GetBaseAttack()*2)
                singleRule(card,EFFECT_EXTRA_ATTACK,1)
            end
        end
    end)
end

function rules.hydradrive(human,ai)
    local function dominated(_,card)
        local ruler=live(ai,perfectron)
        return ruler and card:IsFaceup() and ruler:IsAttribute(card:GetAttribute())
    end
    story.monsterRule(human,EFFECT_DISABLE,dominated)
    story.monsterRule(human,EFFECT_DISABLE_EFFECT,dominated)
end

function rules.storm_core(human,ai)
    local seal=Effect.GlobalEffect()
    seal:SetType(EFFECT_TYPE_FIELD)
    seal:SetCode(EFFECT_DISABLE_FIELD)
    seal:SetValue(((1<<9)|(1<<11))<<(human*16))
    Duel.RegisterEffect(seal,ai)
end

function rules.mirror_link(human,ai)
    story.onPhase(ai,PHASE_STANDBY,function()
        local mirror=live(ai,perfectron)
        if not mirror or not Duel.IsTurnPlayer(ai) then return end
        local links=Duel.GetMatchingGroup(function(card)
            return card:IsFaceup() and card:IsLinkMonster()
        end,human,LOCATION_MZONE,0,nil)
        local image
        for link in aux.Next(links) do
            if not image or link:GetAttack()>image:GetAttack() then image=link end
        end
        if not image then return end
        local code=image:GetCode()
        local reset=RESET_PHASE|PHASE_END
        singleRule(mirror,EFFECT_CHANGE_CODE,code,reset)
        mirror:CopyEffect(code,RESET_EVENT|RESETS_STANDARD|reset,1)
    end)
end

function rules.ignister_arrival(_,ai)
    story.onEvent(ai,EVENT_STARTUP,function()
        local arrival=live(ai,11738489)
        if not arrival then return end
        local attack=Effect.CreateEffect(arrival)
        attack:SetType(EFFECT_TYPE_SINGLE)
        attack:SetCode(EFFECT_SET_BASE_ATTACK)
        attack:SetValue(6000)
        attack:SetReset(RESET_EVENT|RESETS_STANDARD)
        -- Apply this opening stat through its own effect while retaining immunity to other cards.
        arrival:RegisterEffect(attack,true)
    end)
end

function rules.borreload_final(human,ai)
    story.onEvent(ai,EVENT_ATTACK_ANNOUNCE,function()
        local attacker,target=Duel.GetAttacker(),Duel.GetAttackTarget()
        if attacker and attacker==live(ai,31833038) and target and target:IsControler(human) then
            Duel.SendtoHand(target,nil,REASON_EFFECT)
        end
    end)
end

function rules.into_the_vrains(_,ai)
    story.onEvent(ai,EVENT_STARTUP,function()
        local decode=find(ai,LOCATION_EXTRA,1861629) or Duel.CreateToken(ai,1861629)
        if Duel.SendtoGrave(decode,REASON_RULE)>0 then decode:CompleteProcedure() end
        addExtra(ai,98978921)
        placeOnDeck(ai,36211150)
        placeOnDeck(ai,28827503)
    end)
end
