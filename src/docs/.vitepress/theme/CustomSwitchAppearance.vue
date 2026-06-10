<template>
  <button
    class="VPSwitch VPSwitchAppearance"
    type="button"
    role="switch"
    :aria-checked="isDark"
    :title="isDark ? '切换到浅色模式' : '切换到深色模式'"
    @click="toggle"
  >
    <span class="vpi-sun sun" />
    <span class="vpi-moon moon" />
  </button>
</template>

<script setup>
import { useData } from 'vitepress'

const { isDark } = useData()

function toggle(e) {
  const x = e.clientX
  const y = e.clientY
  const endRadius = Math.hypot(
    Math.max(x, innerWidth - x),
    Math.max(y, innerHeight - y)
  )

  if (!document.startViewTransition) {
    isDark.value = !isDark.value
    return
  }

  const transition = document.startViewTransition(() => {
    isDark.value = !isDark.value
  })

  transition.ready.then(() => {
    const nowDark = isDark.value
    const clipPath = [
      `circle(0px at ${x}px ${y}px)`,
      `circle(${endRadius}px at ${x}px ${y}px)`,
    ]
    document.documentElement.animate(
      {
        clipPath: nowDark ? clipPath : [...clipPath].reverse(),
      },
      {
        duration: 500,
        easing: 'ease-in-out',
        pseudoElement: nowDark
          ? '::view-transition-new(root)'
          : '::view-transition-old(root)',
      }
    )
  })
}
</script>
