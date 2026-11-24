local s,id=GetID()

function s.initial_effect(c)
    local play=Effect.CreateEffect(c)
    play:SetType(EFFECT_TYPE_ACTIVATE)
    play:SetCode(EVENT_FREE_CHAIN)
    c:RegisterEffect(play)

    local intercept=Effect.CreateEffect(c)
    intercept:SetType(EFFECT_TYPE_FIELD|EFFECT_TYPE_CONTINUOUS)
    intercept:SetCode(EVENT_CHAIN_SOLVING)
    intercept:SetRange(LOCATION_SZONE)
    intercept:SetCondition(function(_,player,_,opponent,chain,incoming)
        return opponent~=player and incoming:IsHasType(EFFECT_TYPE_ACTIVATE)
            and incoming:IsActiveType(TYPE_SPELL|TYPE_TRAP) and Duel.IsChainDisablable(chain)
    end)
    intercept:SetOperation(function(_,_,_,_,chain,incoming)
        if not Duel.NegateEffect(chain) then return end
        local card=incoming:GetHandler()
        if card:IsOnField() and card:IsRelateToEffect(incoming) then Duel.Destroy(card,REASON_EFFECT) end
    end)
    c:RegisterEffect(intercept)
end
