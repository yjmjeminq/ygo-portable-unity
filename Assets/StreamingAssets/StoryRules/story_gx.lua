local story=YgoStory
local rules=story.rules

function rules.ojama_trio(human,ai)
    story.onEvent(ai,EVENT_STARTUP,function()
        for index=0,2 do
            local token=Duel.CreateToken(ai,TOKEN_OJAMA+index)
            if Duel.SpecialSummonStep(token,0,ai,human,false,false,POS_FACEUP_DEFENSE,1<<(index*2)) then
                local tribute=Effect.CreateEffect(token)
                tribute:SetType(EFFECT_TYPE_SINGLE)
                tribute:SetCode(EFFECT_UNRELEASABLE_SUM)
                tribute:SetProperty(EFFECT_FLAG_CANNOT_DISABLE)
                tribute:SetValue(1)
                tribute:SetReset(RESET_EVENT|RESETS_STANDARD)
                token:RegisterEffect(tribute)

                local damage=Effect.CreateEffect(token)
                damage:SetType(EFFECT_TYPE_SINGLE|EFFECT_TYPE_CONTINUOUS)
                damage:SetCode(EVENT_LEAVE_FIELD)
                damage:SetOperation(function(e)
                    local token=e:GetHandler()
                    if token:IsReason(REASON_DESTROY) then
                        Duel.Damage(token:GetPreviousControler(),300,REASON_EFFECT)
                    end
                    e:Reset()
                end)
                token:RegisterEffect(damage)
            end
        end
        Duel.SpecialSummonComplete()
    end)
end

function rules.six_attributes(human)
    local attributes={ATTRIBUTE_EARTH,ATTRIBUTE_WATER,ATTRIBUTE_FIRE,ATTRIBUTE_WIND,ATTRIBUTE_LIGHT,ATTRIBUTE_DARK}
    local function blocked(_,card)
        local turn=math.max(1,Duel.GetTurnCount(human))
        return card:IsAttribute(attributes[(turn-1)%#attributes+1])
    end
    for _,restriction in ipairs({EFFECT_CANNOT_SUMMON,EFFECT_CANNOT_MSET,EFFECT_CANNOT_FLIP_SUMMON,
        EFFECT_CANNOT_SPECIAL_SUMMON}) do
        story.playerRule(human,restriction,1,nil,blocked)
    end
end

function rules.helios_alchemy(_,ai)
    local function banishedMonster(card) return card:IsFaceup() and card:IsMonster() end
    -- Helios already has 100 ATK per banished monster through its printed effect.
    story.monsterRule(ai,EFFECT_UPDATE_ATTACK,function(_,card) return card:IsCode(54493213) end,function()
        return Duel.GetMatchingGroupCount(banishedMonster,ai,LOCATION_REMOVED,LOCATION_REMOVED,nil)*200
    end)
    story.onPhase(ai,PHASE_STANDBY,function()
        if not Duel.IsExistingMatchingCard(function(c) return c:IsCode(54493213) and c:IsFaceup() end,
            ai,LOCATION_MZONE,0,1,nil) then return end
        local cards=Duel.GetDecktopGroup(Duel.GetTurnPlayer(),1)
        if #cards>0 then Duel.Remove(cards,POS_FACEUP,REASON_EFFECT) end
    end)
end

function rules.vampire_pact(_,ai)
    story.onPhase(ai,PHASE_STANDBY,function(e)
        if Duel.GetTurnPlayer()~=ai or Duel.GetLocationCount(ai,LOCATION_MZONE)<=0 then return end
        local vampires=Duel.GetMatchingGroup(function(c)
            return c:IsSetCard(SET_VAMPIRE) and c:IsCanBeSpecialSummoned(e,0,ai,false,false)
        end,ai,LOCATION_GRAVE,0,nil)
        if #vampires==0 then return end
        local strongest
        for vampire in aux.Next(vampires) do
            if not strongest or vampire:GetAttack()>strongest:GetAttack() then strongest=vampire end
        end
        Duel.SpecialSummon(strongest,0,ai,ai,false,false,POS_FACEUP_ATTACK)
    end)
end

function rules.amazoness_colosseum(human,ai)
    local function arena() return story.activeField(ai,712559) end
    for _,player in ipairs({human,ai}) do
        story.playerRule(player,EFFECT_CANNOT_MSET,1,arena)
        story.playerRule(player,EFFECT_FORCE_SPSUMMON_POSITION,POS_FACEUP_ATTACK,arena)
        story.monsterRule(player,EFFECT_CANNOT_CHANGE_POSITION,nil,1,arena)
        story.monsterRule(player,EFFECT_CANNOT_CHANGE_POS_E,nil,POS_DEFENSE|POS_FACEDOWN_ATTACK,arena)
        story.monsterRule(player,EFFECT_MUST_ATTACK,nil,1,arena)
    end
end

function rules.sacred_beasts(_,ai)
    story.onEvent(ai,EVENT_STARTUP,function()
        local beasts=Duel.GetMatchingGroup(Card.IsCode,ai,LOCATION_MZONE,0,nil,6007213,32491822,69890967)
        for beast in aux.Next(beasts) do
            local ward=Effect.CreateEffect(beast)
            ward:SetType(EFFECT_TYPE_SINGLE|EFFECT_TYPE_CONTINUOUS)
            ward:SetProperty(EFFECT_FLAG_SINGLE_RANGE|EFFECT_FLAG_CANNOT_DISABLE)
            ward:SetRange(LOCATION_MZONE)
            ward:SetCode(EFFECT_DESTROY_REPLACE)
            ward:SetTarget(function(e,_,_,_,_,_,_,_,check)
                if check==0 then return not e:GetHandler():IsReason(REASON_RULE|REASON_REPLACE) end
                Duel.Hint(HINT_CARD,0,e:GetHandler():GetCode())
                return true
            end)
            ward:SetOperation(function(e) e:Reset() end)
            beast:RegisterEffect(ward)
        end
    end)
end

function rules.arcana_fate(_,ai)
    story.onPhase(ai,PHASE_STANDBY,function(e,turn)
        if turn~=1 then return end
        e:Reset()
        local chariot=Duel.CreateToken(ai,34568403)
        Duel.SpecialSummon(chariot,0,ai,ai,false,false,POS_FACEUP_ATTACK,1<<2)
    end)
end

function rules.plasma(human,ai)
    story.onEvent(ai,EVENT_CHAIN_SOLVING,function(_,_,_,ep,chain,incoming)
        if ep~=human or not incoming:IsMonsterEffect() or not Duel.IsChainDisablable(chain) then return end
        if Duel.IsExistingMatchingCard(function(c)
            return c:IsCode(83965310) and c:IsFaceup() and not c:IsDisabled()
        end,ai,LOCATION_MZONE,0,1,nil) then
            Duel.NegateEffect(chain)
        end
    end)
end

function rules.shock_collar(_,ai)
    story.onEvent(ai,EVENT_DESTROYED,function(_,_,cards)
        local hits={[0]=0,0}
        for card in aux.Next(cards) do
            if card:IsPreviousLocation(LOCATION_MZONE) then
                local player=card:GetPreviousControler()
                hits[player]=hits[player]+300
            end
        end
        for player=0,1 do
            if hits[player]>0 then Duel.Damage(player,hits[player],REASON_EFFECT) end
        end
    end)
end

function rules.venom_counters(human,ai)
    local function poison(_,_,cards)
        if not story.activeField(ai,54306223) then return end
        for card in aux.Next(cards) do
            if card:IsControler(human) and card:IsLocation(LOCATION_MZONE) and card:IsFaceup()
                and not card:IsSetCard(SET_VENOM) and card:IsCanAddCounter(COUNTER_VENOM,1) then
                card:AddCounter(COUNTER_VENOM,1)
            end
        end
    end
    for _,event in ipairs({EVENT_SUMMON_SUCCESS,EVENT_FLIP_SUMMON_SUCCESS,EVENT_SPSUMMON_SUCCESS}) do
        story.onEvent(ai,event,poison)
    end
end

function rules.fossil_fusion(_,ai)
    -- A normal draw still happens. Place the theme's fusion spell among the opening cards.
    story.onEvent(ai,EVENT_STARTUP,function()
        local spell=Duel.GetMatchingGroup(Card.IsCode,ai,LOCATION_DECK,0,nil,59419719):GetFirst()
        if not spell then spell=Duel.CreateToken(ai,59419719) end
        Duel.DisableShuffleCheck()
        Duel.SendtoDeck(spell,ai,SEQ_DECKTOP,REASON_EFFECT)
    end)
end

function rules.crystal_beasts(_,ai)
    story.onEvent(ai,EVENT_STARTUP,function()
        local crystals={7093411,32710364,95600067}
        for index,code in ipairs(crystals) do
            local card=Duel.CreateToken(ai,code)
            if Duel.MoveToField(card,ai,ai,LOCATION_SZONE,POS_FACEUP,true,1<<(index-1)) then
                local crystallized=Effect.CreateEffect(card)
                crystallized:SetType(EFFECT_TYPE_SINGLE)
                crystallized:SetCode(EFFECT_CHANGE_TYPE)
                crystallized:SetProperty(EFFECT_FLAG_CANNOT_DISABLE)
                crystallized:SetValue(TYPE_SPELL|TYPE_CONTINUOUS)
                crystallized:SetReset(RESET_EVENT|RESETS_STANDARD)
                card:RegisterEffect(crystallized)
            end
        end
    end)
end

function rules.possessed_beasts(_,ai)
    story.onPhase(ai,PHASE_STANDBY,function(e)
        if Duel.GetTurnPlayer()~=ai then return end
        local beasts=Group.CreateGroup()
        for _,code in ipairs({6007213,32491822,69890967}) do
            local material=Duel.GetMatchingGroup(function(c)
                return c:IsFaceup() and c:IsCode(code) and c:IsAbleToRemove()
            end,ai,LOCATION_MZONE,0,nil):GetFirst()
            if not material then return end
            beasts:AddCard(material)
        end
        local armityle=Duel.GetMatchingGroup(Card.IsCode,ai,LOCATION_EXTRA,0,nil,43378048):GetFirst()
        if not armityle then armityle=Duel.CreateToken(ai,43378048) end
        if not armityle:IsCanBeSpecialSummoned(e,SUMMON_TYPE_FUSION,ai,true,true) then return end
        e:Reset()
        -- Martel fuses the three physical monsters. Removing a beast first breaks the opening.
        if Duel.Remove(beasts,POS_FACEUP,REASON_EFFECT|REASON_MATERIAL|REASON_FUSION)~=3 then return end
        armityle:SetMaterial(beasts)
        if Duel.SpecialSummon(armityle,SUMMON_TYPE_FUSION,ai,ai,true,true,POS_FACEUP_ATTACK)>0 then
            armityle:CompleteProcedure()
        end
    end)
end

function rules.supreme_king_fusion(human,ai)
    story.monsterRule(ai,EFFECT_CANNOT_BE_EFFECT_TARGET,function(_,card)
        return card:IsRace(RACE_FIEND)
    end,function(_,_,source) return source==human end)
end

function rules.super_polymerization_countdown(_,ai)
    story.countdown(ai,10)
end

local zero,infinity=511090101,511090102
local firstDarkness,lastDarkness=511090103,511090105

function rules.darkness_zero_infinity(_,ai)
    story.onEvent(ai,EVENT_STARTUP,function()
        local pieces={zero,infinity,firstDarkness,firstDarkness+1,lastDarkness}
        for index=#pieces,2,-1 do
            local drawn=Duel.GetRandomNumber(1,index)
            pieces[index],pieces[drawn]=pieces[drawn],pieces[index]
        end
        for index,code in ipairs(pieces) do
            Duel.MoveToField(Duel.CreateToken(ai,code),ai,ai,LOCATION_SZONE,POS_FACEDOWN,true,1<<(index-1))
        end
    end)
    story.onPhase(ai,PHASE_STANDBY,function()
        if Duel.GetTurnPlayer()~=ai then return end
        local pieces=Duel.GetMatchingGroup(Card.IsCode,ai,LOCATION_SZONE,0,nil,zero,infinity)
        if pieces:GetClassCount(Card.GetCode)~=2 then return end
        local trigger=pieces:Filter(Card.IsCode,nil,zero):GetFirst()
        if trigger:IsFacedown() then Duel.Activate(trigger:GetActivateEffect()) end
    end)
    story.onPhase(ai,PHASE_END,function()
        if Duel.GetTurnPlayer()~=ai then return end
        local openTraps=Duel.GetMatchingGroup(function(card)
            local code=card:GetCode()
            return card:IsFaceup() and code>=zero and code<=lastDarkness
        end,ai,LOCATION_SZONE,0,nil)
        if #openTraps>0 then Duel.ChangePosition(openTraps,POS_FACEDOWN_DEFENSE) end
    end)
end

function story.darknessLine(player,trigger)
    local other=Duel.GetMatchingGroup(Card.IsCode,player,LOCATION_SZONE,0,nil,infinity):GetFirst()
    if not other or not other:IsFacedown() then return end
    local lo,hi=trigger:GetSequence(),other:GetSequence()
    local step=lo<hi and 1 or -1
    local first
    local revealed=0
    Duel.ChangePosition(other,POS_FACEUP)
    for zone=lo+step,hi-step,step do
        local trap=Duel.GetFieldCard(player,LOCATION_SZONE,zone)
        if trap and trap:IsFacedown() and trap:GetCode()>=firstDarkness and trap:GetCode()<=lastDarkness
            and Duel.ChangePosition(trap,POS_FACEUP)>0 then
            first=first or trap:GetCode()
            revealed=revealed+1
        end
    end
    if not first then return end
    if first==firstDarkness then
        local victims=Duel.GetFieldGroup(player,0,LOCATION_ONFIELD)
        local count=math.min(revealed,#victims)
        if count>0 then Duel.Destroy(victims:Select(player,count,count,nil),REASON_EFFECT) end
    elseif first==firstDarkness+1 then
        local allies=Duel.GetMatchingGroup(Card.IsFaceup,player,LOCATION_MZONE,0,nil)
        if #allies>0 then
            local boosted=allies:Select(player,1,1,nil):GetFirst()
            story.addAttack(boosted,1000*revealed,RESET_PHASE|PHASE_END)
        end
    else
        Duel.Damage(1-player,1000*revealed,REASON_EFFECT)
    end
end

function rules.graduation_duel(_,ai)
    -- Neos and the six Neo-Spacians fuel Divine Neos's native copy effect.
    story.onEvent(ai,EVENT_STARTUP,function()
        local names={89943723,17955766,54959865,89621922,43237273,80344569,17732278}
        local materials=Group.CreateGroup()
        for _,code in ipairs(names) do
            local card=Duel.GetMatchingGroup(Card.IsCode,ai,LOCATION_DECK,0,nil,code):GetFirst()
            materials:AddCard(card or Duel.CreateToken(ai,code))
        end
        Duel.SendtoGrave(materials,REASON_EFFECT)
    end)
end
