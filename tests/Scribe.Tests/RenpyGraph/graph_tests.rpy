# Each case starts a real exported story via Ren'Py's own test runner.
testsuite story_graph:
    teardown:
        exit

    before testcase:
        run Jump("imported_story")
        assert "入口：雨夜选择。" timeout 5.0
        advance until screen "choice" timeout 5.0

    testcase fragment_to_fragment:
        click "进入雨夜"
        assert "雨夜，你终于来了。" timeout 5.0
        advance
        assert "雨停前，我们做出决定。" timeout 5.0
        advance until screen "choice" timeout 5.0
        click "前往回声"
        assert "回声路线：另一条分支。" timeout 5.0
        advance
        assert "共同结局：天亮了。" timeout 5.0

    testcase fragment_to_main:
        click "进入雨夜"
        assert "雨夜，你终于来了。" timeout 5.0
        advance
        assert "雨停前，我们做出决定。" timeout 5.0
        advance until screen "choice" timeout 5.0
        click "回到主线"
        assert "共同结局：天亮了。" timeout 5.0

    testcase skip_to_main_sentence:
        click "直接结局"
        assert "共同结局：天亮了。" timeout 5.0

    testcase backward_jump:
        click "重新开始"
        assert "入口：雨夜选择。" timeout 5.0
        advance until screen "choice" timeout 5.0
        click "直接结局"
        assert "共同结局：天亮了。" timeout 5.0
