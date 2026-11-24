local s,id=GetID()

function s.initial_effect(c)
    if id~=511090101 then return end
    local play=Effect.CreateEffect(c)
    play:SetType(EFFECT_TYPE_ACTIVATE)
    play:SetCode(EVENT_FREE_CHAIN)
    play:SetCondition(function(_,player)
        return YgoStory and YgoStory.darknessLine
            and Duel.IsExistingMatchingCard(function(card) return card:IsFacedown() and card:IsCode(511090102) end,
                player,LOCATION_SZONE,0,1,nil)
    end)
    play:SetOperation(function(e,player)
        if e:GetHandler():IsRelateToEffect(e) then YgoStory.darknessLine(player,e:GetHandler()) end
    end)
    c:RegisterEffect(play)
end
