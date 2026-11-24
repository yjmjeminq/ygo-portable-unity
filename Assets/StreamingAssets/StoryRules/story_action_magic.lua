local s,id=GetID()
local evade=id==511090302

function s.initial_effect(c)
    local fromHand=Effect.CreateEffect(c)
    fromHand:SetType(EFFECT_TYPE_SINGLE)
    fromHand:SetCode(EFFECT_QP_ACT_IN_NTPHAND)
    fromHand:SetProperty(EFFECT_FLAG_CANNOT_DISABLE)
    c:RegisterEffect(fromHand)

    local activate=Effect.CreateEffect(c)
    activate:SetDescription(aux.Stringid(id,0))
    activate:SetType(EFFECT_TYPE_ACTIVATE)
    activate:SetCode(EVENT_ATTACK_ANNOUNCE)
    activate:SetCondition(function(_,player)
        local attacker,target=Duel.GetAttacker(),Duel.GetAttackTarget()
        return attacker and attacker:IsControler(1-player) and
            (evade or target and target:IsControler(player))
    end)
    activate:SetOperation(s.operation)
    c:RegisterEffect(activate)
end

function s.operation(e,player)
    if evade then Duel.NegateAttack() return end
    local target=Duel.GetAttackTarget()
    if not target or not target:IsControler(player) then return end
    local reset=RESET_PHASE|PHASE_DAMAGE|PHASE_END
    local protect=Effect.CreateEffect(e:GetHandler())
    protect:SetType(EFFECT_TYPE_SINGLE)
    protect:SetCode(EFFECT_INDESTRUCTABLE_BATTLE)
    protect:SetValue(1)
    protect:SetReset(RESET_EVENT|RESETS_STANDARD|reset)
    target:RegisterEffect(protect)

    local half=Effect.GlobalEffect()
    half:SetType(EFFECT_TYPE_FIELD)
    half:SetProperty(EFFECT_FLAG_PLAYER_TARGET)
    half:SetTargetRange(1,0)
    half:SetCode(EFFECT_CHANGE_DAMAGE)
    half:SetValue(function(_,_,damage,reason)
        if reason&REASON_BATTLE~=0 and Duel.GetAttackTarget()==target then return math.floor(damage/2) end
        return damage
    end)
    half:SetReset(reset)
    Duel.RegisterEffect(half,player)
end
