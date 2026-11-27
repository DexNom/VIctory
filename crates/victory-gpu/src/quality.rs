//! Texture quality: the setting a game's options menu calls "textures: high,
//! medium, low".
//!
//! **Off.** The reduction below is not shipped: it removes the device on the
//! title's loading screen (`0x887A0001`, an invalid call on the host device
//! from the copy path). Every texture is uploaded whole, as before, until
//! every copy that touches a host copy is proven to shift its subresources
//! with the copy's first level. `crate::resource::base_floor` is the one
//! place that reads this, and with 0 the host copies are exactly what they
//! were before the setting existed.
//!
//! What it was meant to do: a read-only texture the title places in memory is
//! uploaded from the first level its views show on
//! ([`crate::resource::held_only`]). Beginning the host copy one level lower
//! quarters what the largest level costs: a quarter of the bytes read from
//! title memory, a quarter of the card's memory, a quarter of what a refresh
//! walks. The picture loses its largest level, which is what a lower texture
//! setting buys everywhere: soft up close, the same far away.
//!
//! `VICTORY_TEXTURE_QUALITY`: `high` (0), `medium` (1), `low` (2), clamped to
//! four. The level is clamped to what the format's blocks allow and to the
//! levels the title's own views start at ([`crate::resource::base_level`]), so
//! a texture whose only levels are the small ones keeps all of them.

/// How many levels of a texture's top the host copy skips (0: none).
pub(crate) fn mip_bias() -> u32 {
    0
}

#[cfg(test)]
mod tests {
    /// Enquanto a reducao estiver desligada, nenhuma copia comeca abaixo do
    /// nivel 0: e o que mantem o caminho de copia identico ao que rodava.
    #[test]
    fn the_reduction_is_off() {
        assert_eq!(super::mip_bias(), 0);
    }
}
