# Minimal test-only UI; not part of Jumu's exported scripts.
define config.name = "Jumu 0.4.0 跳转测试"
define config.screen_width = 1280
define config.screen_height = 720
define config.check_conflicting_properties = True

style default:
    font ("SourceHanSansLite.ttf" if renpy.loadable("SourceHanSansLite.ttf") else "DejaVuSans.ttf")
    size 32

screen main_menu():
    tag menu
    add Solid("#173a34")
    vbox:
        align (0.5, 0.5)
        spacing 30
        text "句幕 0.4.0 · 素材袋跳转测试"
        textbutton "Start" action Start()
        textbutton "Quit" action Quit(confirm=False)

screen say(who, what):
    window:
        background Solid("#173a34")
        xfill True
        yalign 1.0
        padding (45, 30)
        vbox:
            spacing 16
            if who:
                text who id "who"
            text what id "what"

screen choice(items):
    vbox:
        align (0.5, 0.5)
        spacing 18
        for item in items:
            textbutton item.caption action item.action
