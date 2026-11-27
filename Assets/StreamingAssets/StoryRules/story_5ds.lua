local story=YgoStory
local rules=story.rules

local function faceupMonster(player,code)
    return Duel.GetMatchingGroup(function(card)
        return card:IsCode(code) and card:IsFaceup() and not card:IsDisabled()
    end,player,LOCATION_MZONE,0,nil):GetFirst()
end

function rules.handcuff_tokens(human,ai)
    story.onEvent(ai,EVENT_STARTUP,function()
        for _,zone in ipairs({1<<1,1<<3}) do
            local cuff=Duel.CreateToken(ai,511090201)
            if Duel.SpecialSummonStep(cuff,0,ai,human,false,false,POS_FACEUP_DEFENSE,zone) then
                for _,restriction in ipairs({EFFECT_CANNOT_ATTACK,EFFECT_UNRELEASABLE_SUM}) do
                    local locked=Effect.CreateEffect(cuff)
                    locked:SetType(EFFECT_TYPE_SINGLE)
                    locked:SetCode(restriction)
                    locked:SetProperty(EFFECT_FLAG_CANNOT_DISABLE)
                    locked:SetValue(1)
                    cuff:RegisterEffect(locked)
                end
            end
        end
        Duel.SpecialSummonComplete()
    end)
end

function rules.prison_penalty(human,ai)
    local function punishSet(_,_,cards)
        local count=cards:FilterCount(Card.IsControler,nil,human)
        if count>0 then Duel.Damage(human,200*count,REASON_EFFECT) end
    end
    story.onEvent(ai,EVENT_MSET,punishSet)
    story.onEvent(ai,EVENT_SSET,punishSet)
    story.onEvent(ai,EVENT_CHAINING,function(_,_,_,player)
        if player==human then Duel.Damage(human,200,REASON_EFFECT) end
    end)
end

local function punishSummons(human,ai,source,amount)
    local function punish(_,_,cards)
        if cards:IsExists(Card.IsControler,1,nil,human) and faceupMonster(ai,source) then
            Duel.Damage(human,amount,REASON_EFFECT)
        end
    end
    for _,event in ipairs({EVENT_SUMMON_SUCCESS,EVENT_FLIP_SUMMON_SUCCESS,EVENT_SPSUMMON_SUCCESS}) do
        story.onEvent(ai,event,punish)
    end
end

function rules.black_rose_thorns(human,ai)
    punishSummons(human,ai,73580471,300)
end

function rules.monkey_prison(human,ai)
    story.playerRule(human,EFFECT_CANNOT_SPECIAL_SUMMON,1,
        function() return faceupMonster(ai,22858242)~=nil end,
        function(_,_,_,summonType) return summonType&SUMMON_TYPE_SYNCHRO==SUMMON_TYPE_SYNCHRO end)
end

rules.riding_monkey_prison=rules.monkey_prison

function rules.riding_petrification(human,ai)
    story.monsterRule(human,EFFECT_SET_ATTACK_FINAL,nil,0,
        function() return faceupMonster(ai,89810518)~=nil end)
end

function rules.riding_underworld_altar(_,ai)
    -- Inti's printed effect can revive Quilla from this real Graveyard.
    story.onEvent(ai,EVENT_STARTUP,function()
        local moon=Duel.GetMatchingGroup(Card.IsCode,ai,LOCATION_EXTRA,0,nil,66818682):GetFirst()
        if not moon then moon=Duel.CreateToken(ai,66818682) end
        if Duel.SendtoGrave(moon,REASON_RULE)>0 then moon:CompleteProcedure() end
    end)
end

function rules.riding_unicorn()
    story.playerRule(0,EFFECT_BP_FIRST_TURN,1)
end

-- These scenes begin partway through their respective riding rounds.
local function beyondDestinyOpening(counters)
    return function(human)
        local function anotherFieldSpell(card)
            return card and card:IsType(TYPE_FIELD) and not card:IsCode(511090001)
        end
        for player=0,1 do
            -- Speed World remains the track while the usual Monster and Spell/Trap Zones work normally.
            story.playerRule(player,EFFECT_CANNOT_ACTIVATE,function(_,effect)
                return effect:IsHasType(EFFECT_TYPE_ACTIVATE) and anotherFieldSpell(effect:GetHandler())
            end)
            story.playerRule(player,EFFECT_CANNOT_SSET,1,nil,function(_,card)
                return anotherFieldSpell(card)
            end)
        end
        story.onEvent(human,EVENT_STARTUP,function()
            for player=0,1 do
                local field=Duel.GetFieldCard(player,LOCATION_FZONE,0)
                if field and field:IsCode(511090001) then
                    local function protect(code,value)
                        local effect=Effect.CreateEffect(field)
                        effect:SetType(EFFECT_TYPE_SINGLE)
                        effect:SetCode(code)
                        effect:SetProperty(EFFECT_FLAG_CANNOT_DISABLE|EFFECT_FLAG_UNCOPYABLE)
                        effect:SetValue(value)
                        field:RegisterEffect(effect)
                    end
                    protect(EFFECT_IMMUNE_EFFECT,function(_,incoming)
                        return incoming:GetHandler()~=field
                    end)
                    protect(EFFECT_CANNOT_BE_EFFECT_TARGET,1)
                    field:AddCounter(0x0091,counters)
                end
            end
        end)
    end
end
rules.riding_beyond_destiny_1=beyondDestinyOpening(1)
rules.riding_beyond_destiny_4=beyondDestinyOpening(4)
rules.riding_beyond_destiny_6=beyondDestinyOpening(6)

function rules.riding_odin(_,ai)
    story.monsterRule(ai,EFFECT_IMMUNE_EFFECT,function(_,card)
        return card:IsCode(93483212)
    end,function(_,incoming)
        return incoming:IsActiveType(TYPE_SPELL|TYPE_TRAP)
    end)
end

function rules.riding_granel(_,ai)
    story.monsterRule(ai,EFFECT_SET_ATTACK_FINAL,function(_,card)
        return card:IsCode(4545683)
    end,function()
        return math.max(8000,math.floor(Duel.GetLP(ai)/2))
    end,function() return Duel.GetTurnCount()<=2 end)
end

local function openWithSynchro(ai,boss,parts)
    story.onEvent(ai,EVENT_STARTUP,function()
        if Duel.IsExistingMatchingCard(Card.IsCode,ai,LOCATION_EXTRA,0,1,nil,boss) then return end
        Duel.SendtoDeck(Duel.CreateToken(ai,boss),ai,SEQ_DECKTOP,REASON_RULE)
    end)
    local attempted=false
    story.onEvent(ai,EVENT_PHASE_START+PHASE_MAIN1,function()
        if attempted or Duel.GetTurnPlayer()~=ai then return end
        local materials=Group.CreateGroup()
        for _,code in ipairs(parts) do
            local material=Duel.GetMatchingGroup(function(card)
                return card:IsFaceup() and card:IsCode(code)
            end,ai,LOCATION_MZONE,0,nil):GetFirst()
            if not material then return end
            materials:AddCard(material)
        end
        local dragon=Duel.GetMatchingGroup(Card.IsCode,ai,LOCATION_EXTRA,0,nil,boss):GetFirst()
        if not dragon or not dragon:IsSynchroSummonable(nil,materials,#parts,#parts) then return end
        attempted=true
        Duel.SynchroSummon(ai,dragon,nil,materials,#parts,#parts)
    end)
end

function rules.riding_delta_accel(_,ai)
    openWithSynchro(ai,97836203,{98558751,62560742,24943456})
end

function rules.riding_timelords(_,ai)
    story.onEvent(ai,EVENT_DAMAGE,function(e,_,_,player,damage)
        if player~=ai or Duel.GetLP(ai)<=0 or not faceupMonster(ai,8967776) then return end
        -- Rewind the first nonlethal loss without reversing other resolved moves.
        e:Reset()
        Duel.SetLP(ai,Duel.GetLP(ai)+damage)
    end)
end

function rules.riding_stardust_finale(_,ai)
    openWithSynchro(ai,24696097,{44508094,50091196})
end
