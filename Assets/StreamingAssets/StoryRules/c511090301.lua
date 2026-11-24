local s,id=GetID()
local evasion,miracle=511090302,511090303

function s.initial_effect(c)
    local play=Effect.CreateEffect(c)
    play:SetType(EFFECT_TYPE_ACTIVATE)
    play:SetCode(EVENT_FREE_CHAIN)
    c:RegisterEffect(play)

    local search=Effect.CreateEffect(c)
    search:SetDescription(aux.Stringid(id,0))
    search:SetType(EFFECT_TYPE_IGNITION)
    search:SetRange(LOCATION_FZONE)
    search:SetCountLimit(1,id)
    search:SetCategory(CATEGORY_TOHAND)
    search:SetCondition(function(_,player)
        return not Duel.IsExistingMatchingCard(Card.IsCode,player,LOCATION_HAND,0,1,nil,evasion,miracle)
    end)
    search:SetOperation(function(e,player)
        if not e:GetHandler():IsRelateToEffect(e) then return end
        local card=Duel.CreateToken(player,Duel.GetRandomNumber(0,1)==0 and evasion or miracle)
        if Duel.SendtoHand(card,player,REASON_EFFECT)>0 then Duel.ConfirmCards(1-player,card) end
    end)
    c:RegisterEffect(search)
end
